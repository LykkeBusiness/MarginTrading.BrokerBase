using Lykke.MarginTrading.BrokerBase.Models;

namespace Lykke.MarginTrading.BrokerBase.Settings
{
    public class BrokersLogsSettings
    {
        public StorageMode StorageMode { get; set; }
        
        public string LogsConnString { get; set; }
    }
}