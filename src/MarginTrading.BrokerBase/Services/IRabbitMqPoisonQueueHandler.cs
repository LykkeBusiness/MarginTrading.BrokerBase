namespace Lykke.MarginTrading.BrokerBase.Services
{
    public interface IRabbitMqPoisonQueueHandler
    {
        string TryPutMessagesBack();
    }
}