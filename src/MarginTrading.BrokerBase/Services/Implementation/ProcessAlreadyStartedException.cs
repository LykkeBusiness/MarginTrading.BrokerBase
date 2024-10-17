using System;

namespace Lykke.MarginTrading.BrokerBase.Services.Implementation;

internal sealed class ProcessAlreadyStartedException : Exception
{
    public ProcessAlreadyStartedException(string message) : base(message)
    {
    }
}
