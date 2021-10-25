using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Common.Log;
using JetBrains.Annotations;
using Lykke.MarginTrading.BrokerBase.Extensions;
using Lykke.MarginTrading.BrokerBase.Models;
using Lykke.MarginTrading.BrokerBase.Settings;
using Lykke.RabbitMqBroker;
using Lykke.RabbitMqBroker.Publisher.Serializers;
using Lykke.RabbitMqBroker.Subscriber;
using Lykke.RabbitMqBroker.Subscriber.Deserializers;
using Lykke.RabbitMqBroker.Subscriber.MessageReadStrategies;
using Lykke.RabbitMqBroker.Subscriber.Middleware;
using Lykke.RabbitMqBroker.Subscriber.Middleware.ErrorHandling;
using Lykke.SlackNotifications;
using Lykke.Snow.Common.Correlation.RabbitMq;
using Microsoft.Extensions.Logging;

namespace Lykke.MarginTrading.BrokerBase
{
    [UsedImplicitly]
    public abstract class BrokerApplicationBase<TMessage> : IBrokerApplication
    {
        private readonly RabbitMqCorrelationManager _correlationManager;
        protected readonly ILoggerFactory LoggerFactory;
        protected readonly ILog Logger;
        private readonly ISlackNotificationsSender _slackNotificationsSender;
        protected readonly CurrentApplicationInfo ApplicationInfo;
        private readonly ConcurrentDictionary<int, IStartStop> _subscribers = new ConcurrentDictionary<int, IStartStop>();
        protected readonly IMessageDeserializer<TMessage> MessageDeserializer;
        protected readonly IRabbitMqSerializer<TMessage> MessageSerializer;

        protected abstract BrokerSettingsBase Settings { get; }
        protected abstract string ExchangeName { get; }
        public abstract string RoutingKey { get; }
        protected virtual string QueuePostfix => string.Empty;
        
        protected DateTime LastMessageTime { get; private set; }

        protected BrokerApplicationBase(RabbitMqCorrelationManager correlationManager, ILoggerFactory loggerFactory, ILog logger, ISlackNotificationsSender slackNotificationsSender,
            CurrentApplicationInfo applicationInfo, MessageFormat messageFormat = MessageFormat.Json)
        {
            _correlationManager = correlationManager;
            LoggerFactory = loggerFactory;
            Logger = logger;
            _slackNotificationsSender = slackNotificationsSender;
            ApplicationInfo = applicationInfo;
            MessageDeserializer = messageFormat == MessageFormat.Json
                ? new JsonMessageDeserializer<TMessage>()
                : (IMessageDeserializer<TMessage>) new MessagePackMessageDeserializer<TMessage>();
            MessageSerializer = messageFormat == MessageFormat.Json
                ? new JsonMessageSerializer<TMessage>()
                : (IRabbitMqSerializer<TMessage>) new MessagePackMessageSerializer<TMessage>();
        }
        
        public RabbitMqSubscriptionSettings GetRabbitMqSubscriptionSettings()
        {
            var settings = new RabbitMqSubscriptionSettings
            {
                ConnectionString = Settings.MtRabbitMqConnString,
                QueueName = QueueHelper.BuildQueueName(ExchangeName, Settings.Env, QueuePostfix),
                ExchangeName = ExchangeName,
                IsDurable = true,
                DeadLetterExchangeName = QueueHelper.BuildDeadLetterExchangeName(ExchangeName),
            };
            if (!string.IsNullOrWhiteSpace(RoutingKey))
            {
                settings.RoutingKey = RoutingKey;
            }
            return settings;
        }

        protected abstract Task HandleMessage(TMessage message);

        private Task HandleMessageWithThrottling(TMessage message)
        {
            var messageTime = DateTime.UtcNow;

            if (messageTime.Subtract(LastMessageTime).TotalSeconds > (1 / Settings.ThrottlingRateThreshold))
            {
                LastMessageTime = messageTime;
                return HandleMessage(message);
            }
            
            return Task.CompletedTask;
        }

        /// <summary>
        /// By default the pipeline is ResilientErrorHandlingStrategy (1 sec, 5 times) followed by DeadQueueErrorHandlingStrategy.
        /// Override if own strategy required.
        /// </summary>
        [UsedImplicitly]
        protected virtual IEnumerable<IEventMiddleware<TMessage>> GetMiddlewares<TMessage>(RabbitMqSubscriptionSettings settings)
        {
            return GetDefaultMiddlewares<TMessage>(settings);
        }

        protected IEnumerable<IEventMiddleware<TMessage>> GetDefaultMiddlewares<TMessage>(RabbitMqSubscriptionSettings settings)
        {
            return new List<IEventMiddleware<TMessage>>
            {
                new ResilientErrorHandlingMiddleware<TMessage>(
                    LoggerFactory.CreateLogger<ResilientErrorHandlingMiddleware<TMessage>>(),
                    TimeSpan.FromSeconds(1)),
                new DeadQueueMiddleware<TMessage>(LoggerFactory.CreateLogger<DeadQueueMiddleware<TMessage>>())
            };
        }

        public virtual void Run()
        {
            WriteInfoToLogAndSlack("Starting listening exchange " + ExchangeName);

            try
            {
                var subscriptionSettings = GetRabbitMqSubscriptionSettings();
                var consumerCount = Settings.ConsumerCount <= 0 ? 1 : Settings.ConsumerCount;

                foreach (var consumerNumber in Enumerable.Range(1, consumerCount))
                {
                    var rabbitMqSubscriber = BuildSubscriber(subscriptionSettings, HandleMessage, HandleMessageWithThrottling);

                    if (!_subscribers.TryAdd(consumerNumber, rabbitMqSubscriber))
                    {
                        throw new InvalidOperationException(
                            $"Subscriber #{consumerNumber} for queue {subscriptionSettings.QueueName} was already initialized");
                    }

                    rabbitMqSubscriber.Start();
                }

                WriteInfoToLogAndSlack("Broker listening queue " + subscriptionSettings.QueueName);
            }
            catch (Exception ex)
            {
                Logger.WriteErrorAsync(ApplicationInfo.ApplicationFullName, "Application.RunAsync", null, ex).GetAwaiter()
                    .GetResult();
            }
        }

        private RabbitMqSubscriber<TMessage> BuildSubscriber(RabbitMqSubscriptionSettings subscriptionSettings,
            Func<TMessage, Task> basicHandler, Func<TMessage, Task> throttlingHandler)
        {
            var result = new RabbitMqSubscriber<TMessage>(
                LoggerFactory.CreateLogger<RabbitMqSubscriber<TMessage>>(),
                subscriptionSettings)
            .SetMessageDeserializer(MessageDeserializer)
            .SetMessageReadStrategy(new MessageReadWithTemporaryQueueStrategy(RoutingKey ?? ""))
            .SetReadHeadersAction(_correlationManager.FetchCorrelationIfExists)
            .Subscribe(msg => Settings.ThrottlingRateThreshold.HasValue
                ? throttlingHandler(msg)
                : basicHandler(msg));
            
            foreach (var middleware in GetMiddlewares<TMessage>(subscriptionSettings))
            {
                result = result.UseMiddleware(middleware);
            }
            
            return result;
        }

        public void StopApplication()
        {
            Console.WriteLine($"Closing {ApplicationInfo.ApplicationFullName}...");
            WriteInfoToLogAndSlack("Stopping listening exchange " + ExchangeName);
            foreach (var subscriber in _subscribers.Values)
            {
                subscriber.Stop();
            }
        }
        
        public byte[] RepackMessage(byte[] serializedMessage)
        {
            TMessage message;
            try
            {
                message = MessageDeserializer.Deserialize(serializedMessage);
            }
            catch (Exception exception)
            {
                Logger.WriteErrorAsync(this.GetType().Name, nameof(RepackMessage),
                    $"Failed to deserialize the message: {serializedMessage} with {MessageDeserializer.GetType().Name}. Stopping.", 
                    exception).GetAwaiter().GetResult();
                return null;
            }

            return MessageSerializer.Serialize(message);
        }

        protected void WriteInfoToLogAndSlack(string infoMessage)
        {
            Logger.WriteInfo(ApplicationInfo.ApplicationFullName, null, infoMessage);
            _slackNotificationsSender.SendAsync(ChannelTypes.MarginTrading, ApplicationInfo.ApplicationFullName,
                infoMessage);
        }
    }
}
