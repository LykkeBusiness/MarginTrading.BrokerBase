using System;
using System.Threading;

namespace Lykke.MarginTrading.BrokerBase.Services.Implementation;

internal sealed class ParallelExecutionGuardPoisonQueueDecorator : IRabbitMqPoisonQueueHandler
{
    private readonly IRabbitMqPoisonQueueHandler _decoratee;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly TimeSpan _timeout = TimeSpan.FromMinutes(10);

    public ParallelExecutionGuardPoisonQueueDecorator(IRabbitMqPoisonQueueHandler decoratee)
    {
        _decoratee = decoratee ?? throw new ArgumentNullException(nameof(decoratee));
    }

    public string TryPutMessagesBack() => _lock.Execute(_decoratee.TryPutMessagesBack, _timeout);
}
