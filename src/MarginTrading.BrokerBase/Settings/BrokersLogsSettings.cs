using MarginTrading.BrokerBase.Models;

namespace MarginTrading.BrokerBase.Settings
{
    public class BrokersLogsSettings
    {
        public StorageMode StorageMode { get; set; }
        
        public string DbConnString { get; set; }
    }
}