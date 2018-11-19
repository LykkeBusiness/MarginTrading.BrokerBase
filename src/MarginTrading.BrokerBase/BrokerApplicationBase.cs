using System;
using System.Threading.Tasks;
using Common.Log;
using JetBrains.Annotations;
using Lykke.MarginTrading.BrokerBase.Extensions;
using Lykke.MarginTrading.BrokerBase.Models;
using Lykke.MarginTrading.BrokerBase.Settings;
using Lykke.RabbitMqBroker;
using Lykke.RabbitMqBroker.Subscriber;
using Lykke.SlackNotifications;

namespace Lykke.MarginTrading.BrokerBase
{
    [UsedImplicitly]
    public abstract class BrokerApplicationBase<TMessage> : IBrokerApplication
    {
        protected readonly ILog Logger;
        private readonly ISlackNotificationsSender _slackNotificationsSender;
        protected readonly CurrentApplicationInfo ApplicationInfo;
        private RabbitMqSubscriber<TMessage> _connector;
        private readonly MessageFormat _messageFormat;

        protected abstract BrokerSettingsBase Settings { get; }
        protected abstract string ExchangeName { get; }
        protected abstract string RoutingKey { get; }
        protected virtual string QueuePostfix => string.Empty;
        
        protected DateTime LastMessageTime { get; private set; }
        
        private RabbitMqSubscriptionSettings GetRabbitMqSubscriptionSettings()
        {
            var settings = new RabbitMqSubscriptionSettings
            {
                ConnectionString = Settings.MtRabbitMqConnString,
                QueueName = QueueHelper.BuildQueueName(ExchangeName, Settings.Env, QueuePostfix),
                ExchangeName = ExchangeName,
                IsDurable = true
            };
            if (!string.IsNullOrWhiteSpace(RoutingKey))
            {
                settings.RoutingKey = RoutingKey;
            }
            return settings;
        }

        protected abstract Task HandleMessage(TMessage message);

        protected BrokerApplicationBase(ILog logger, ISlackNotificationsSender slackNotificationsSender,
            CurrentApplicationInfo applicationInfo, MessageFormat messageFormat = MessageFormat.Json)
        {
            Logger = logger;
            _slackNotificationsSender = slackNotificationsSender;
            ApplicationInfo = applicationInfo;
            _messageFormat = messageFormat;
        }

        private Task HandleMessageWithThrottling(TMessage message)
        {
            var messageTime = DateTime.UtcNow;

            if (messageTime.Subtract(LastMessageTime).TotalSeconds > (1 / Settings.ThrottlingRateThreshold))
            {
                LastMessageTime = messageTime;
                return HandleMessage(message);
            }
            
            return Task.CompletedTask;
        }

        public virtual void Run()
        {
            WriteInfoToLogAndSlack("Starting listening exchange " + ExchangeName);
            
            try
            {
                var settings = GetRabbitMqSubscriptionSettings();
                _connector = new RabbitMqSubscriber<TMessage>(settings,
                        new ResilientErrorHandlingStrategy(Logger, settings, TimeSpan.FromSeconds(1)))
                    .SetMessageDeserializer(_messageFormat == MessageFormat.Json
                        ? new JsonMessageDeserializer<TMessage>()
                        : (IMessageDeserializer<TMessage>)new MessagePackMessageDeserializer<TMessage>())
                    .SetMessageReadStrategy(new MessageReadWithTemporaryQueueStrategy(RoutingKey ?? ""))
                    .Subscribe(msg => Settings.ThrottlingRateThreshold.HasValue 
                        ? HandleMessageWithThrottling(msg)
                        : HandleMessage(msg))
                    .SetLogger(Logger)
                    .Start();

                WriteInfoToLogAndSlack("Broker listening queue " + settings.QueueName);
            }
            catch (Exception ex)
            {
                Logger.WriteErrorAsync(ApplicationInfo.ApplicationFullName, "Application.RunAsync", null, ex).GetAwaiter()
                    .GetResult();
            }
        }

        public void StopApplication()
        {
            Console.WriteLine($"Closing {ApplicationInfo.ApplicationFullName}...");
            WriteInfoToLogAndSlack("Stopping listening exchange " + ExchangeName);
            _connector.Stop();
        }

        protected void WriteInfoToLogAndSlack(string infoMessage)
        {
            Logger.WriteInfoAsync(ApplicationInfo.ApplicationFullName, null, null, infoMessage);
            _slackNotificationsSender.SendAsync(ChannelTypes.MarginTrading, ApplicationInfo.ApplicationFullName,
                infoMessage);
        }
    }
}
