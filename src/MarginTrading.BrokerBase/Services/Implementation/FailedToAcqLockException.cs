using System;

namespace Lykke.MarginTrading.BrokerBase.Services.Implementation;

internal sealed class FailedToAcqLockException : Exception
{
    public FailedToAcqLockException(string message) : base(message) { }
}
