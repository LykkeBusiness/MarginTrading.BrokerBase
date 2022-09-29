namespace Lykke.MarginTrading.BrokerBase.Settings
{
    public class DefaultBrokerApplicationSettings<TBrokerSettings>: IBrokerApplicationSettings<TBrokerSettings>
        where TBrokerSettings: BrokerSettingsBase
    {
        public BrokersLogsSettings MtBrokersLogs { get; set; }
        
        public TBrokerSettings MtBrokerSettings { get; set; }
    }
}
