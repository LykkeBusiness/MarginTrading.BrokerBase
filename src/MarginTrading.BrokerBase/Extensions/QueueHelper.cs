using Microsoft.Extensions.PlatformAbstractions;

namespace Lykke.MarginTrading.BrokerBase.Extensions
{
    public static class QueueHelper
    {
        public static string BuildQueueName(string exchangeName, string env, string postfix = "")
        {
            return
                $"{exchangeName}.{PlatformServices.Default.Application.ApplicationName}.{env ?? "DefaultEnv"}{postfix}";
        }
        
        public static string BuildDeadLetterExchangeName(string exchangeName)
        {
            return $"{exchangeName}.dlx";
        }
    }
}