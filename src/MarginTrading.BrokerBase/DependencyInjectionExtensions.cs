using Autofac;

using Lykke.MarginTrading.BrokerBase.Messaging;
using Lykke.RabbitMqBroker;

using Microsoft.Extensions.DependencyInjection;

namespace Lykke.MarginTrading.BrokerBase
{
    public static class DependencyInjectionExtensions
    {
        /// <summary>
        /// Add a JSON messaging factory for the specified message type.
        /// Also adds a RabbitMQ connection provider.
        /// </summary>
        /// <param name="src"></param>
        /// <typeparam name="TMessage"></typeparam>
        /// <returns></returns>
        public static IServiceCollection AddJsonBrokerMessagingFactory<TMessage>(this IServiceCollection src)
        {
            src.AddRabbitMqConnectionProvider();
            src.AddSingleton<IMessagingComponentFactory<TMessage>, JsonMessagingComponentFactory<TMessage>>();
            return src;
        }
        
        /// <summary>
        /// Add a MessagePack messaging factory for the specified message type.
        /// Also adds a RabbitMQ connection provider.
        /// </summary>
        /// <param name="src"></param>
        /// <typeparam name="TMessage"></typeparam>
        /// <returns></returns>
        public static IServiceCollection AddMessagePackBrokerMessagingFactory<TMessage>(this IServiceCollection src)
        {
            src.AddRabbitMqConnectionProvider();
            src.AddSingleton<IMessagingComponentFactory<TMessage>, MessagingPackMessagingComponentFactory<TMessage>>();
            return src;
        }
        
        /// <summary>
        /// Add a JSON messaging factory for the specified message type.
        /// Also adds a RabbitMQ connection provider.
        /// </summary>
        /// <param name="src"></param>
        /// <typeparam name="TMessage"></typeparam>
        /// <returns></returns>
        public static ContainerBuilder AddJsonBrokerMessagingFactory<TMessage>(this ContainerBuilder src)
        {
            src.AddRabbitMqConnectionProvider();
            src.RegisterType<JsonMessagingComponentFactory<TMessage>>()
                .As<IMessagingComponentFactory<TMessage>>()
                .SingleInstance();
            return src;
        }
        
        /// <summary>
        /// Add a MessagePack messaging factory for the specified message type.
        /// Also adds a RabbitMQ connection provider.
        /// </summary>
        /// <param name="src"></param>
        /// <typeparam name="TMessage"></typeparam>
        /// <returns></returns>
        public static ContainerBuilder AddMessagePackBrokerMessagingFactory<TMessage>(this ContainerBuilder src)
        {
            src.AddRabbitMqConnectionProvider();
            src.RegisterType<MessagingPackMessagingComponentFactory<TMessage>>()
                .As<IMessagingComponentFactory<TMessage>>()
                .SingleInstance();
            return src;
        }
    }
}