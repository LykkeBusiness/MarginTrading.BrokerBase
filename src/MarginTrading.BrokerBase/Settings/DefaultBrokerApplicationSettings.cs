using JetBrains.Annotations;
using Lykke.SettingsReader.Attributes;

namespace Lykke.MarginTrading.BrokerBase.Settings
{
    [UsedImplicitly]
    public class DefaultBrokerApplicationSettings<TBrokerSettings>: IBrokerApplicationSettings<TBrokerSettings>
        where TBrokerSettings: BrokerSettingsBase
    {
        [Optional]
        public SlackNotificationSettings SlackNotifications { get; set; }
        
        public BrokersLogsSettings MtBrokersLogs { get; set; }
        
        public TBrokerSettings MtBrokerSettings { get; set; }
    }
}
