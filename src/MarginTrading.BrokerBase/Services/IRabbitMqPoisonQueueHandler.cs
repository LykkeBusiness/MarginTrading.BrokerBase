using System.Threading.Tasks;

namespace Lykke.MarginTrading.BrokerBase.Services
{
    public interface IRabbitMqPoisonQueueHandler
    {
        Task<string> PutMessagesBack();
    }
}