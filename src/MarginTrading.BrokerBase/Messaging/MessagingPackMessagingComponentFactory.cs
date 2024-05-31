using System;

using Lykke.RabbitMqBroker;
using Lykke.RabbitMqBroker.Publisher.Serializers;
using Lykke.RabbitMqBroker.Subscriber;
using Lykke.RabbitMqBroker.Subscriber.Deserializers;

using Microsoft.Extensions.Logging;

namespace Lykke.MarginTrading.BrokerBase.Messaging
{
    internal sealed class MessagingPackMessagingComponentFactory<TMessage> : IMessagingComponentFactory<TMessage>
    {
        private readonly IConnectionProvider _connectionProvider;
        private readonly ILoggerFactory _loggerFactory;

        public MessagingPackMessagingComponentFactory(
            IConnectionProvider connectionProvider,
            ILoggerFactory loggerFactory)
        {
            _connectionProvider = connectionProvider;
            _loggerFactory = loggerFactory;
        }

        public IMessageDeserializer<TMessage> CreateDeserializer() => new MessagePackMessageDeserializer<TMessage>();

        public IRabbitMqSerializer<TMessage> CreateSerializer() => new MessagePackMessageSerializer<TMessage>();

        /// <summary>
        /// Creates a "no loss" subscriber with MessagePack message format.
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
            var subscriber = RabbitMqSubscriber<TMessage>.MessagePack.CreateNoLossSubscriber(
                subscriptionSettings,
                connection,
                _loggerFactory);

            configure?.Invoke(subscriber);

            return subscriber;
        }
    }
}