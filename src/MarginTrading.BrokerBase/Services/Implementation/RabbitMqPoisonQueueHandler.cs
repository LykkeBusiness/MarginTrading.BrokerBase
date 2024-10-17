using Lykke.MarginTrading.BrokerBase.Extensions;
using Lykke.MarginTrading.BrokerBase.Settings;
using Lykke.RabbitMqBroker;

using Microsoft.Extensions.Logging;

using RabbitMQ.Client;

namespace Lykke.MarginTrading.BrokerBase.Services.Implementation;

public class RabbitMqPoisonQueueHandler : IRabbitMqPoisonQueueHandler
{
    private readonly string _connectionString;
    private readonly IBrokerApplication _brokerApplication;
    private readonly RabbitMqSubscriptionSettings _rabbitMqSubscriptionSettings;
    private readonly IConnectionProvider _connectionProvider;
    private readonly ILogger _logger;
    private readonly RequeueConfiguration _requeueConfiguration;
    private string PoisonQueueName => QueueHelper.BuildDeadLetterQueueName(_rabbitMqSubscriptionSettings.QueueName);
    private uint _initialMessagesCount;
    private uint _messagesRequeued;

    public RabbitMqPoisonQueueHandler(
        BrokerSettingsBase brokerSettingsBase,
        IBrokerApplication brokerApplication,
        IConnectionProvider connectionProvider,
        ILogger<RabbitMqPoisonQueueHandler> logger)
    {
        _connectionString = brokerSettingsBase.MtRabbitMqConnString;
        _brokerApplication = brokerApplication;
        _rabbitMqSubscriptionSettings = brokerApplication.GetRabbitMqSubscriptionSettings();
        _logger = logger;
        _connectionProvider = connectionProvider;
        _requeueConfiguration = RequeueConfiguration.Create(
            PoisonQueueName,
            _rabbitMqSubscriptionSettings.ExchangeName,
            _brokerApplication.RoutingKey);
    }

    public string TryPutMessagesBack()
    {
        using var connection = _connectionProvider.GetExclusive(_connectionString);
        using var channel = connection.CreateModel();

        EnsureResourcesExistOrThrow(channel, out _initialMessagesCount);

        _messagesRequeued = _initialMessagesCount switch
        {
            > 0 => new PoisonQueueConsumer(channel, _requeueConfiguration).Start(),
            _ => default
        };

        return BuildResultText();
    }

    private void EnsureResourcesExistOrThrow(IModel channel, out uint messagesCount)
    {
        messagesCount = channel.QueueDeclarePassive(PoisonQueueName).MessageCount;
        channel.ExchangeDeclarePassive(_rabbitMqSubscriptionSettings.ExchangeName);

        _logger.LogInformation("{MessagesCount} messages found in poison queue.", messagesCount);
    }

    private string BuildResultText() => _messagesRequeued switch
    {
        0 => string.Empty,
        _ => $"Messages requeue finished. Initial number of messages {_initialMessagesCount}. Processed number of messages {_messagesRequeued}"
    };
}