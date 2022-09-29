using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lykke.MarginTrading.BrokerBase.Settings;
using Lykke.RabbitMqBroker;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Framing;

namespace Lykke.MarginTrading.BrokerBase.Services.Implementation
{
    public class RabbitPoisonHandingService : IRabbitPoisonHandingService, IDisposable
    {
        private readonly BrokerSettingsBase _brokerSettingsBase;
        private readonly IBrokerApplication _brokerApplication;
        private readonly RabbitMqSubscriptionSettings _rabbitMqSubscriptionSettings;
        private readonly List<IModel> _channels = new List<IModel>();
        private IConnection _connection;
        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);
        private readonly ILogger _logger;

        private string PoisonQueueName => $"{_rabbitMqSubscriptionSettings.QueueName}-poison";

        public RabbitPoisonHandingService(BrokerSettingsBase brokerSettingsBase, 
            IBrokerApplication brokerApplication, ILogger<RabbitPoisonHandingService> logger)
        {
            _brokerSettingsBase = brokerSettingsBase;
            _brokerApplication = brokerApplication;
            _rabbitMqSubscriptionSettings = brokerApplication.GetRabbitMqSubscriptionSettings();
            _logger = logger;
        }
        
        public async Task<string> PutMessagesBack()
        {
            if (_semaphoreSlim.CurrentCount == 0)
            {
                throw new Exception($"Cannot start the process because it was already started and not yet finished.");
            }

            await _semaphoreSlim.WaitAsync(TimeSpan.FromMinutes(10));

            try
            {
                var factory = new ConnectionFactory {Uri = new Uri(_brokerSettingsBase.MtRabbitMqConnString, UriKind.Absolute)};
                _logger.LogInformation("Trying to connect to {Endpoint} ({ExchangeName})", factory.Endpoint,
                    _rabbitMqSubscriptionSettings.ExchangeName);

                _connection = factory.CreateConnection();

                var publishingChannel = _connection.CreateModel();
                var subscriptionChannel = _connection.CreateModel();
                _channels.AddRange(new[] {publishingChannel, subscriptionChannel});

                var publishingArgs = new Dictionary<string, object>()
                {
                    {"x-dead-letter-exchange", _rabbitMqSubscriptionSettings.DeadLetterExchangeName}
                };

                subscriptionChannel.QueueDeclare(PoisonQueueName,
                    _rabbitMqSubscriptionSettings.IsDurable, false, false, null);

                var messagesFound = subscriptionChannel.MessageCount(PoisonQueueName);
                var processedMessages = 0;
                string result;

                if (messagesFound == 0)
                {
                    result = "No messages found in poison queue. Terminating the process";
                    
                    _logger.LogWarning(result);
                    FreeResources();
                    return result;
                }

                _logger.LogInformation("{MessagesCount} messages found in poison queue. Starting the process", messagesFound);

                publishingChannel.QueueDeclare(_rabbitMqSubscriptionSettings.QueueName,
                    _rabbitMqSubscriptionSettings.IsDurable, false, false, publishingArgs);

                var consumer = new EventingBasicConsumer(subscriptionChannel);
                consumer.Received += (ch, ea) =>
                {
                    var message = _brokerApplication.RepackMessage(ea.Body);

                    if (message != null)
                    {
                        try
                        {
                            IBasicProperties properties = null;
                            if (!string.IsNullOrEmpty(_brokerApplication.RoutingKey) ||
                                ea.BasicProperties?.Headers?.Count > 0)
                            {
                                properties = new BasicProperties();
                                if (!string.IsNullOrEmpty(_brokerApplication.RoutingKey))
                                {
                                    properties.Type = _brokerApplication.RoutingKey;
                                }
                                if (ea.BasicProperties?.Headers?.Count > 0)
                                {
                                    properties.Headers = ea.BasicProperties.Headers;
                                }
                            }

                            publishingChannel.BasicPublish(_rabbitMqSubscriptionSettings.ExchangeName,
                                _brokerApplication.RoutingKey ?? "", properties, message);

                            subscriptionChannel.BasicAck(ea.DeliveryTag, false);

                            processedMessages++;
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "Error resending message: {Message}", e.Message);
                        }
                    }
                };

                var sw = new Stopwatch();
                sw.Start();
                
                var tag = subscriptionChannel.BasicConsume(PoisonQueueName, false,
                    consumer);

                _logger.LogInformation("Consumer {Tag} started", tag);

                while (processedMessages < messagesFound)
                {
                    Thread.Sleep(100);

                    if (sw.ElapsedMilliseconds > 30000)
                    {
                        _logger.LogWarning("Messages resend takes more than 30s. Terminating the process");
                        break;
                    }
                }

                result =
                    $"Messages resend finished. Initial number of messages {messagesFound}. Processed number of messages {processedMessages}";
                _logger.LogInformation(result);
                FreeResources();
                return result;
            }
            catch (Exception exception)
            {
                var result =
                    $"Exception [{exception.Message}] thrown while putting messages back from poison to queue {_rabbitMqSubscriptionSettings.QueueName}. Stopping the process.";
                _logger.LogError(exception, result);
                return result;
            }
        }

        private void FreeResources()
        {
            foreach (var channel in _channels)
            {
                channel?.Close();
                channel?.Dispose();
            }
            _connection?.Close();
            _connection?.Dispose();
            
            _semaphoreSlim.Release();
            
            _logger.LogInformation("Channels and connection disposed");
        }

        public void Dispose()
        {
            FreeResources();
        }
    }
}