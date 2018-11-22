using JetBrains.Annotations;
using Lykke.SettingsReader.Attributes;

namespace Lykke.MarginTrading.BrokerBase.Settings
{
    [UsedImplicitly]
    public class BrokerSettingsBase
    {
        public string MtRabbitMqConnString { get; set; }
        
        [Optional]
        public string Env { get; set; }
        
        /// <summary>
        /// If set handler will throttle all messages that exceeds the rate in seconds.
        /// </summary>
        [Optional]
        public int? ThrottlingRateThreshold { get; set; }
    }
}