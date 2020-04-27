using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Common.Log;
using JetBrains.Annotations;
using Lykke.MarginTrading.BrokerBase.Extensions;
using Lykke.MarginTrading.BrokerBase.Models;
using Lykke.MarginTrading.BrokerBase.Settings;
using Lykke.RabbitMqBroker;
using Lykke.RabbitMqBroker.Publisher;
using Lykke.RabbitMqBroker.Subscriber;
using Lykke.SlackNotifications;

namespace Lykke.MarginTrading.BrokerBase
{
    [UsedImplicitly]
    public abstract class BrokerApplicationBase<TMessage> : IBrokerApplication
    {
        protected readonly ILog Logger;
        private readonly ISlackNotificationsSender _slackNotificationsSender;
        protected readonly CurrentApplicationInfo ApplicationInfo;
        private readonly ConcurrentDictionary<int, IStopable> _subscribers = new ConcurrentDictionary<int, IStopable>();
        protected readonly IMessageDeserializer<TMessage> MessageDeserializer;
        protected readonly IRabbitMqSerializer<TMessage> MessageSerializer;

        protected abstract BrokerSettingsBase Settings { get; }
        protected abstract string ExchangeName { get; }
        public abstract string RoutingKey { get; }
        protected virtual string QueuePostfix => string.Empty;
        
        protected DateTime LastMessageTime { get; private set; }

        protected BrokerApplicationBase(ILog logger, ISlackNotificationsSender slackNotificationsSender,
            CurrentApplicationInfo applicationInfo, MessageFormat messageFormat = MessageFormat.Json)
        {
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
        protected virtual IErrorHandlingStrategy GetErrorHandlingStrategy(RabbitMqSubscriptionSettings settings)
        {
            return GetDefaultErrorHandlingStrategy(settings);
        }

        protected IErrorHandlingStrategy GetDefaultErrorHandlingStrategy(RabbitMqSubscriptionSettings settings) =>
            new ResilientErrorHandlingStrategy(Logger, settings, TimeSpan.FromSeconds(1),
                next: new DeadQueueErrorHandlingStrategy(Logger, settings));

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
            => new RabbitMqSubscriber<TMessage>(subscriptionSettings,
                    new ResilientErrorHandlingStrategy(Logger, subscriptionSettings, TimeSpan.FromSeconds(1)))
                .SetMessageDeserializer(MessageDeserializer)
                .SetMessageReadStrategy(new MessageReadWithTemporaryQueueStrategy(RoutingKey ?? ""))
                .Subscribe(msg => Settings.ThrottlingRateThreshold.HasValue
                    ? throttlingHandler(msg)
                    : basicHandler(msg))
                .SetLogger(Logger);

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
