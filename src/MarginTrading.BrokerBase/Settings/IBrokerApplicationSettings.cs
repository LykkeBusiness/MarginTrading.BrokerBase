using JetBrains.Annotations;
using Lykke.SettingsReader.Attributes;

namespace MarginTrading.BrokerBase.Settings
{
    public interface IBrokerApplicationSettings<TBrokerSettings> 
        where TBrokerSettings : BrokerSettingsBase
    {
        [Optional, CanBeNull]
        SlackNotificationSettings SlackNotifications { get; }
        
        [Optional, CanBeNull]
        BrokersLogsSettings MtBrokersLogs { get; set; }
        
        TBrokerSettings MtBrokerSettings { get; set; }
    }
}
