using Lykke.MarginTrading.BrokerBase.Models;
using Lykke.SettingsReader.Attributes;

namespace Lykke.MarginTrading.BrokerBase.Settings
{
    public class BrokersLogsSettings
    {
        public StorageMode StorageMode { get; set; }
        
        [Optional]
        public bool WriteToFile { get; set; }
        
        public string LogsConnString { get; set; }
    }
}