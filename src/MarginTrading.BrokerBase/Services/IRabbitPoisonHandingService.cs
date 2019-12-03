using System.Threading.Tasks;
using Lykke.RabbitMqBroker.Publisher;
using Lykke.RabbitMqBroker.Subscriber;

namespace Lykke.MarginTrading.BrokerBase.Services
{
    public interface IRabbitPoisonHandingService
    {
        Task PutMessagesBack();
    }
}