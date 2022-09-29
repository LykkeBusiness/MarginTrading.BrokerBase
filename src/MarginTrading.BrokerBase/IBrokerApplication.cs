using Lykke.RabbitMqBroker;

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