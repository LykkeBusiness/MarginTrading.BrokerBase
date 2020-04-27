using System.Threading.Tasks;

namespace Lykke.MarginTrading.BrokerBase.Services
{
    public interface IRabbitPoisonHandingService
    {
        Task<string> PutMessagesBack();
    }
}