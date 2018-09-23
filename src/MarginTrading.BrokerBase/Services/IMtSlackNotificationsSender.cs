using System.Threading.Tasks;
using Lykke.SlackNotifications;

namespace Lykke.MarginTrading.BrokerBase.Services
{
    public interface IMtSlackNotificationsSender : ISlackNotificationsSender
    {
        Task SendRawAsync(string type, string sender, string message);
    }
}