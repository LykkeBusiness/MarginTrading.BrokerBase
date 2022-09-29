namespace Lykke.MarginTrading.BrokerBase.Settings
{
    public interface IBrokerApplicationSettings<TBrokerSettings> 
        where TBrokerSettings : BrokerSettingsBase
    {
        BrokersLogsSettings MtBrokersLogs { get; set; }
        
        TBrokerSettings MtBrokerSettings { get; set; }
    }
}
