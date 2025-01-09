﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Lykke.MarginTrading.BrokerBase.Extensions;
using Lykke.MarginTrading.BrokerBase.Messaging;
using Lykke.MarginTrading.BrokerBase.Settings;
using Lykke.RabbitMqBroker;
using Lykke.RabbitMqBroker.Publisher.Serializers;
using Lykke.RabbitMqBroker.Subscriber;
using Lykke.RabbitMqBroker.Subscriber.Deserializers;
using Lykke.RabbitMqBroker.Subscriber.Middleware;
using Lykke.RabbitMqBroker.Subscriber.Middleware.ErrorHandling;
using Lykke.Snow.Common.Correlation.RabbitMq;
using Microsoft.Extensions.Logging;

using Serilog;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Lykke.MarginTrading.BrokerBase
{
    public abstract class BrokerApplicationBase<TMessage> : IBrokerApplication
    {
        private readonly RabbitMqCorrelationManager _correlationManager;
        private readonly ConcurrentDictionary<int, IStartStop> _subscribers = new ConcurrentDictionary<int, IStartStop>();
        private readonly IMessagingComponentFactory<TMessage> _messagingComponentFactory;
        
        protected readonly IMessageDeserializer<TMessage> MessageDeserializer;
        protected readonly IRabbitMqSerializer<TMessage> MessageSerializer;
        protected readonly CurrentApplicationInfo ApplicationInfo;
        protected readonly ILoggerFactory LoggerFactory;
        protected readonly ILogger Logger;

        protected abstract BrokerSettingsBase Settings { get; }
        protected abstract string ExchangeName { get; }
        public abstract string RoutingKey { get; }
        protected virtual string QueuePostfix => string.Empty;
        
        protected DateTime LastMessageTime { get; private set; }

        private const int PrefetchCount = 200;
        
        protected BrokerApplicationBase(RabbitMqCorrelationManager correlationManager,
            ILoggerFactory loggerFactory,
            CurrentApplicationInfo applicationInfo,
            IMessagingComponentFactory<TMessage> messagingComponentFactory)
        {
            _correlationManager = correlationManager ?? throw new ArgumentNullException(nameof(correlationManager));
            ApplicationInfo = applicationInfo ?? throw new ArgumentNullException(nameof(applicationInfo));
            _messagingComponentFactory = messagingComponentFactory ??
                                         throw new ArgumentNullException(nameof(messagingComponentFactory));
            MessageDeserializer = _messagingComponentFactory.CreateDeserializer();
            MessageSerializer = _messagingComponentFactory.CreateSerializer();
            LoggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            Logger = loggerFactory.CreateLogger<BrokerApplicationBase<TMessage>>();
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
        protected virtual IEnumerable<IEventMiddleware<TMessage>> GetMiddlewares(RabbitMqSubscriptionSettings settings)
        {
            return GetDefaultMiddlewares(settings);
        }

        protected IEnumerable<IEventMiddleware<TMessage>> GetDefaultMiddlewares(RabbitMqSubscriptionSettings settings)
        {
            return new List<IEventMiddleware<TMessage>>
            {
                new DeadQueueMiddleware<TMessage>(LoggerFactory.CreateLogger<DeadQueueMiddleware<TMessage>>()),
                new ResilientErrorHandlingMiddleware<TMessage>(
                    LoggerFactory.CreateLogger<ResilientErrorHandlingMiddleware<TMessage>>(),
                    TimeSpan.FromSeconds(1))
            };
        }

        public virtual void Run()
        {
            Logger.LogInformation("Starting listening exchange {ExchangeName}", ExchangeName);

            try
            {
                var subscriptionSettings = GetRabbitMqSubscriptionSettings();
                var consumerCount = Settings.ConsumerCount <= 0 ? 1 : Settings.ConsumerCount;

                foreach (var consumerNumber in Enumerable.Range(1, consumerCount))
                {
                    var rabbitMqSubscriber =
                        BuildSubscriber(subscriptionSettings, HandleMessage, HandleMessageWithThrottling);

                    if (!_subscribers.TryAdd(consumerNumber, rabbitMqSubscriber))
                    {
                        throw new InvalidOperationException(
                            $"Subscriber #{consumerNumber} for queue {subscriptionSettings.QueueName} was already initialized");
                    }

                    rabbitMqSubscriber.Start();
                }
                
                Log.Information("Broker listening queue {QueueName}", subscriptionSettings.QueueName);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error while starting subscribers. Rethrowing.");
                throw;
            }
        }

        private RabbitMqSubscriber<TMessage> BuildSubscriber(
            RabbitMqSubscriptionSettings subscriptionSettings,
            Func<TMessage, Task> basicHandler,
            Func<TMessage, Task> throttlingHandler)
        {
            var subscriber = _messagingComponentFactory.CreateSubscriber(
                subscriptionSettings,
                s =>
                {
                    s.SetReadHeadersAction(_correlationManager.FetchCorrelationIfExists)
                        .SetPrefetchCount(PrefetchCount)
                        .Subscribe(
                            msg => Settings.ThrottlingRateThreshold.HasValue
                                ? throttlingHandler(msg)
                                : basicHandler(msg));


                    foreach (var middleware in GetMiddlewares(subscriptionSettings))
                    {
                        s.UseMiddleware(middleware);
                    }
                });

            return subscriber;
        }

        public void StopApplication()
        {
            Console.WriteLine($"Closing {ApplicationInfo.ApplicationFullName}...");
            Logger.LogInformation("Stopping listening exchange {ExchangeName}", ExchangeName);
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
            catch (Exception)
            {
                Logger.LogError(
                    "Failed to deserialize the message: {SerializedMessage} with {MessageDeserializerName}, stopping",
                    serializedMessage, MessageDeserializer.GetType().Name);
                return null;
            }

            return MessageSerializer.Serialize(message);
        }
    }
}
