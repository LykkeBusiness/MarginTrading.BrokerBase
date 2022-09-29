using Lykke.MarginTrading.BrokerBase.Models;
using Lykke.SettingsReader.Attributes;

namespace Lykke.MarginTrading.BrokerBase.Settings
{
    public class BrokersLogsSettings
    {
        [Optional] 
        public StorageMode StorageMode { get; set; } = StorageMode.SqlServer;
        
        [Optional]
        public string LogsConnString { get; set; }
        
        [Optional]
        public bool UseSerilog { get; set; }
    }
}