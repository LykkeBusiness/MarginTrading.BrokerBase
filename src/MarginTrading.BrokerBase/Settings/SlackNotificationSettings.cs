using JetBrains.Annotations;
using Lykke.AzureQueueIntegration;

namespace Lykke.MarginTrading.BrokerBase.Settings
{
    [UsedImplicitly]
    public class SlackNotificationSettings
    {
        public AzureQueueSettings AzureQueue { get; set; }
    }
}
