namespace MarginTrading.BrokerBase.Tests;

using Lykke.MarginTrading.BrokerBase.Services.Implementation;

using RabbitMQ.Client;

public sealed class PoisonQueueConsumerTests
{
    [Fact]
    public void Should_Process_Messages()
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri("uri", UriKind.Absolute),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(60),
            ContinuationTimeout = TimeSpan.FromSeconds(30),
            ClientProvidedName = "PoisonQueueConsumerTests"
        };

        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();

        var consumer = new PoisonQueueConsumer(channel, RequeueConfiguration.Create("poisonQueueName", "exchangeQueueName", null));

        var count = consumer.Start();

        Assert.True(count > 0);
    }
}