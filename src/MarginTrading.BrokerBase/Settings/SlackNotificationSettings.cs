using Lykke.AzureQueueIntegration;

namespace Lykke.MarginTrading.BrokerBase.Settings
{
    public class SlackNotificationSettings
    {
        public AzureQueueSettings AzureQueue { get; set; }
    }
}
