using System;

using Lykke.RabbitMqBroker;
using Lykke.RabbitMqBroker.Publisher.Serializers;
using Lykke.RabbitMqBroker.Subscriber;
using Lykke.RabbitMqBroker.Subscriber.Deserializers;

namespace Lykke.MarginTrading.BrokerBase.Messaging
{
    /// <summary>
    /// Abstract factory interface to exclude dependency on message format
    /// </summary>
    /// <typeparam name="TMessage"></typeparam>
    public interface IMessagingComponentFactory<TMessage>
    {
        /// <summary>
        /// Create message deserializer.
        /// </summary>
        /// <returns></returns>
        IMessageDeserializer<TMessage> CreateDeserializer();
        
        /// <summary>
        /// Create message serializer.
        /// </summary>
        /// <returns></returns>
        IRabbitMqSerializer<TMessage> CreateSerializer();

        /// <summary>
        /// Create subscriber with specified settings.
        /// </summary>
        /// <param name="subscriptionSettings"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        RabbitMqSubscriber<TMessage> CreateSubscriber(
            RabbitMqSubscriptionSettings subscriptionSettings,
            Action<RabbitMqSubscriber<TMessage>> configure = null);
    }
}