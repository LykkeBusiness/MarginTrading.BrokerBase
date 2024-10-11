using System;

using Lykke.RabbitMqBroker;
using Lykke.RabbitMqBroker.Publisher.Serializers;
using Lykke.RabbitMqBroker.Subscriber;
using Lykke.RabbitMqBroker.Subscriber.Deserializers;

using Microsoft.Extensions.Logging;

namespace Lykke.MarginTrading.BrokerBase.Messaging
{
    internal sealed class JsonMessagingComponentFactory<TMessage> : IMessagingComponentFactory<TMessage>
    {
        private readonly IConnectionProvider _connectionProvider;
        private readonly ILoggerFactory _loggerFactory;

        public JsonMessagingComponentFactory(IConnectionProvider connectionProvider, ILoggerFactory loggerFactory)
        {
            _connectionProvider = connectionProvider;
            _loggerFactory = loggerFactory;
        }

        public IRabbitMqSerializer<TMessage> CreateSerializer() => new JsonMessageSerializer<TMessage>();

        public IMessageDeserializer<TMessage> CreateDeserializer() => new JsonMessageDeserializer<TMessage>();

        /// <summary>
        /// Creates a "no loss" subscriber with JSON message format.
        /// Relies on shared connection provided by <see cref="IConnectionProvider"/>.
        /// </summary>
        /// <param name="subscriptionSettings"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public RabbitMqSubscriber<TMessage> CreateSubscriber(
            RabbitMqSubscriptionSettings subscriptionSettings,
            Action<RabbitMqSubscriber<TMessage>> configure = null)
        {
            var connection = _connectionProvider.GetOrCreateShared(subscriptionSettings.ConnectionString);
            var subscriber = RabbitMqSubscriber<TMessage>.Json.CreateNoLossSubscriber(
                subscriptionSettings,
                connection,
                _loggerFactory);
            
            configure?.Invoke(subscriber);
            
            return subscriber;
        }
    }
}