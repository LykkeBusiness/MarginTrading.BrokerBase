using System;

using RabbitMQ.Client;

namespace Lykke.MarginTrading.BrokerBase.Services.Implementation;

/// <summary>
/// Consumer for poison queue to requeue messages back to the original exchange.
/// Not thread safe.
/// </summary>
public class PoisonQueueConsumer(IModel channel, RequeueConfiguration configuration)
{
    public uint Start()
    {
        channel.ConfirmSelect();

        uint processedMessages = 0;
        while (TryRequeueOne()) { processedMessages++; }

        return processedMessages;
    }

    private bool TryRequeueOne()
    {
        var result = channel.BasicGet(configuration.PoisonQueueName, false);
        if (result == null)
        {
            return false;
        }

        try
        {
            channel.BasicPublish(
                configuration.ExchangeName,
                configuration.RoutingKey,
                CreatePropertiesFrom(result.BasicProperties),
                Copy(result.Body));
            channel.WaitForConfirmsOrDie();
            channel.BasicAck(result.DeliveryTag, false);
            return true;
        }
        catch (Exception)
        {
            channel.BasicNack(result.DeliveryTag, false, true);
            throw;
        }
    }

    private IBasicProperties CreatePropertiesFrom(IBasicProperties source)
    {
        IBasicProperties properties = null;

        if (!string.IsNullOrEmpty(configuration.RoutingKey))
        {
            properties = channel.CreateBasicProperties();
            properties.Type = configuration.RoutingKey;
        }

        if (source?.Headers?.Count > 0)
        {
            properties ??= channel.CreateBasicProperties();
            properties.Headers = source.Headers;
        }

        return properties;
    }

    private static byte[] Copy(ReadOnlyMemory<byte> source)
    {
        var copy = new byte[source.Length];
        Buffer.BlockCopy(source.ToArray(), 0, copy, 0, source.Length);
        return copy;
    }
}
