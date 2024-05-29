using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lykke.MarginTrading.BrokerBase.Extensions;
using Lykke.MarginTrading.BrokerBase.Settings;
using Lykke.RabbitMqBroker;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Lykke.MarginTrading.BrokerBase.Services.Implementation
{
    public class RabbitMqPoisonQueueHandler : IRabbitMqPoisonQueueHandler, IDisposable
    {
        private readonly string _connectionString;
        private readonly IBrokerApplication _brokerApplication;
        private readonly RabbitMqSubscriptionSettings _rabbitMqSubscriptionSettings;
        private readonly List<IModel> _channels = new List<IModel>();
        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);
        private readonly IConnectionProvider _connectionProvider;
        private readonly ILogger _logger;

        private string PoisonQueueName => QueueHelper.BuildDeadLetterQueueName(_rabbitMqSubscriptionSettings.QueueName);

        public RabbitMqPoisonQueueHandler(BrokerSettingsBase brokerSettingsBase,
                                          IBrokerApplication brokerApplication,
                                          IConnectionProvider connectionProvider,
                                          ILogger<RabbitMqPoisonQueueHandler> logger)
        {
            _connectionString = brokerSettingsBase.MtRabbitMqConnString;
            _brokerApplication = brokerApplication;
            _rabbitMqSubscriptionSettings = brokerApplication.GetRabbitMqSubscriptionSettings();
            _logger = logger;
            _connectionProvider = connectionProvider;
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
                var connection = _connectionProvider.GetExclusive(_connectionString);
                _logger.LogInformation("Trying to connect to {Endpoint} ({ExchangeName})", connection.Endpoint,
                    _rabbitMqSubscriptionSettings.ExchangeName);
                var publishingChannel = connection.CreateModel();
                var subscriptionChannel = connection.CreateModel();
                _channels.AddRange(new[] { publishingChannel, subscriptionChannel });

                var publishingArgs = new Dictionary<string, object>()
                {
                    { "x-dead-letter-exchange", _rabbitMqSubscriptionSettings.DeadLetterExchangeName }
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

                _logger.LogInformation("{MessagesCount} messages found in poison queue. Starting the process",
                    messagesFound);

                publishingChannel.QueueDeclare(_rabbitMqSubscriptionSettings.QueueName,
                    _rabbitMqSubscriptionSettings.IsDurable, false, false, publishingArgs);

                var consumer = new EventingBasicConsumer(subscriptionChannel);
                consumer.Received += (ch, ea) =>
                {
                    var message = _brokerApplication.RepackMessage(ea.Body.ToArray());

                    if (message != null)
                    {
                        try
                        {
                            IBasicProperties properties = null;
                            if (!string.IsNullOrEmpty(_brokerApplication.RoutingKey) ||
                                ea.BasicProperties?.Headers?.Count > 0)
                            {
                                properties = new BrokerBasicProperties();
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
                    $"Exception [{exception.Message}] thrown while putting messages back from poison queue {PoisonQueueName} to queue {_rabbitMqSubscriptionSettings.QueueName}. Stopping the process.";
                _logger.LogError(exception, result);
                return result;
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        private void FreeResources()
        {
            foreach (var channel in _channels)
            {
                channel?.Close();
                channel?.Dispose();
            }
            
            _logger.LogInformation("Channels disposed");
        }

        public void Dispose()
        {
            FreeResources();
        }
    }
}