using System;
using System.Threading.Tasks;
using Lykke.RabbitMqBroker.Publisher;
using Lykke.RabbitMqBroker.Subscriber;

namespace Lykke.MarginTrading.BrokerBase
{
    public interface IBrokerApplication
    {
        void Run();
        void StopApplication();

        string RoutingKey { get; }
        
        RabbitMqSubscriptionSettings GetRabbitMqSubscriptionSettings();

        byte[] RepackMessage(byte[] serializedMessage);
    }
}