using System;

namespace Lykke.MarginTrading.BrokerBase.Services.Implementation;

public record RequeueConfiguration(string PoisonQueueName, string ExchangeName, string RoutingKey)
{
    public static RequeueConfiguration Create(string poisonQueueName, string exchangeName, string routingKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(poisonQueueName, nameof(poisonQueueName));
        ArgumentException.ThrowIfNullOrWhiteSpace(exchangeName, nameof(exchangeName));

        return new(poisonQueueName, exchangeName, routingKey ?? string.Empty);
    }
}
