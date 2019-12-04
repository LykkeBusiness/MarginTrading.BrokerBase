using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Log;
using Lykke.MarginTrading.BrokerBase.Settings;
using Lykke.RabbitMqBroker;
using Lykke.RabbitMqBroker.Publisher;
using Lykke.RabbitMqBroker.Subscriber;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Lykke.MarginTrading.BrokerBase.Services.Implementation
{
    public class RabbitPoisonHandingService : IRabbitPoisonHandingService, IDisposable
    {
        private readonly BrokerSettingsBase _brokerSettingsBase;
        private readonly IBrokerApplication _brokerApplication;
        private readonly RabbitMqSubscriptionSettings _rabbitMqSubscriptionSettings;
        private readonly ILog _log;
        private readonly List<IModel> _channels = new List<IModel>();
        private IConnection _connection;
        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);

        private string PoisonQueueName => $"{_rabbitMqSubscriptionSettings.QueueName}-poison";

        public RabbitPoisonHandingService(BrokerSettingsBase brokerSettingsBase, 
            IBrokerApplication brokerApplication, ILog log)
        {
            _brokerSettingsBase = brokerSettingsBase;
            _brokerApplication = brokerApplication;
            _rabbitMqSubscriptionSettings = brokerApplication.GetRabbitMqSubscriptionSettings();
            _log = log;
        }
        
        public async Task PutMessagesBack()
        {
            if (_semaphoreSlim.CurrentCount == 0)
            {
                throw new Exception($"Cannot start the process because it was already started and not yet finished.");
            }

            await _semaphoreSlim.WaitAsync(TimeSpan.FromMinutes(10));

            try
            {
                var factory = new ConnectionFactory {Uri = _brokerSettingsBase.MtRabbitMqConnString};
                await _log.WriteInfoAsync(nameof(RabbitPoisonHandingService), nameof(PutMessagesBack),
                    $"Trying to connect to {factory.Endpoint} ({_rabbitMqSubscriptionSettings.ExchangeName})");

                _connection = factory.CreateConnection();

                var publishingChannel = _connection.CreateModel();
                var subscriptionChannel = _connection.CreateModel();
                _channels.AddRange(new[] {publishingChannel, subscriptionChannel});

                publishingChannel.QueueDeclare(_rabbitMqSubscriptionSettings.QueueName,
                    _rabbitMqSubscriptionSettings.IsDurable, false, false, null);

                subscriptionChannel.QueueDeclare(PoisonQueueName, 
                    _rabbitMqSubscriptionSettings.IsDurable, false, false, null);

                var consumer = new EventingBasicConsumer(subscriptionChannel);
                consumer.Received += (ch, ea) =>
                {
                    var message = _brokerApplication.RepackMessage(ea.Body);

                    if (message != null)
                    {
                        publishingChannel.BasicPublish("", _brokerApplication.RoutingKey, null, message);
                        
                        subscriptionChannel.BasicAck(ea.DeliveryTag, false);
                        
                        subscriptionChannel.WaitForConfirms();
                        publishingChannel.WaitForConfirms();
                        
                        if (subscriptionChannel.MessageCount(PoisonQueueName) != 0)
                        {
                            return;
                        }
                    }
                    
                    FreeResources();//todo is this kind of termination ok ??? test it!
                };
                
                var tag = subscriptionChannel.BasicConsume(_rabbitMqSubscriptionSettings.QueueName, false,
                    consumer);
                
                await _log.WriteInfoAsync(nameof(RabbitPoisonHandingService), nameof(PutMessagesBack),
                    $"Consumer {tag} started.");
            }
            catch (Exception exception)
            {
                await _log.WriteErrorAsync(nameof(RabbitPoisonHandingService), nameof(PutMessagesBack),
                    $"Exception thrown while putting messages back from poison to queue {_rabbitMqSubscriptionSettings.QueueName}. Stopping the process.",
                    exception);
                
                FreeResources();
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
            
            _log.WriteInfo(nameof(RabbitPoisonHandingService), nameof(FreeResources),
                $"Channels and connection disposed.");
        }

        public void Dispose()
        {
            FreeResources();
        }
    }
}