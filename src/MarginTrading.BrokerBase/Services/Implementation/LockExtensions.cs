using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lykke.MarginTrading.BrokerBase.Services.Implementation;

internal static class LockExtensions
{
    public static async Task<T> Execute<T>(this SemaphoreSlim semaphore, Func<Task<T>> func, TimeSpan waitTimeout)
    {
        if (semaphore.CurrentCount == 0)
        {
            throw new ProcessAlreadyStartedException("The lock has already been acquired");
        }

        if (!await semaphore.WaitAsync(waitTimeout))
        {
            throw new FailedToAcqLockException($"Failed to acquire lock within the specified timeout: {waitTimeout}");
        }

        try
        {
            return await func();
        }
        finally
        {
            semaphore.Release();
        }
    }
}
