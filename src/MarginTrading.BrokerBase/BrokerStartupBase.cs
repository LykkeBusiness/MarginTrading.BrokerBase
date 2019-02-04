using System;
using System.IO;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Common.Log;
using Lykke.AzureQueueIntegration;
using Lykke.Logs;
using Lykke.Logs.MsSql;
using Lykke.Logs.MsSql.Repositories;
using Lykke.Logs.Serilog;
using Lykke.MarginTrading.BrokerBase.Extensions;
using Lykke.MarginTrading.BrokerBase.Models;
using Lykke.MarginTrading.BrokerBase.Services;
using Lykke.MarginTrading.BrokerBase.Services.Implementation;
using Lykke.MarginTrading.BrokerBase.Settings;
using Lykke.SettingsReader;
using Lykke.SlackNotification.AzureQueue;
using Lykke.SlackNotifications;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.PlatformAbstractions;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Lykke.MarginTrading.BrokerBase
{
    public abstract class BrokerStartupBase<TApplicationSettings, TSettings>
        where TApplicationSettings : class, IBrokerApplicationSettings<TSettings>
        where TSettings: BrokerSettingsBase
    {
        public IConfigurationRoot Configuration { get; }
        public IHostingEnvironment Environment { get; }
        public IContainer ApplicationContainer { get; private set; }
        public ILog Log { get; private set; }

        protected abstract string ApplicationName { get; }

        protected BrokerStartupBase(IHostingEnvironment env)
        {
            Configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddDevJson(env)
                .AddSerilogJson(env)
                .AddEnvironmentVariables()
                .Build();

            Environment = env;
        }

        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            var loggerFactory = new LoggerFactory()
                .AddConsole(LogLevel.Error)
                .AddDebug(LogLevel.Warning);

            services.AddSingleton(loggerFactory);
            services.AddLogging();
            services.AddSingleton(Configuration);
            services.AddMvc();

            var applicationSettings = Configuration.LoadSettings<TApplicationSettings>(
                    throwExceptionOnCheckError: !Configuration.NotThrowExceptionsOnServiceValidation())
                .Nested(s =>
                {
                    var settings = s.MtBrokerSettings;
                    if (!string.IsNullOrEmpty(Configuration["Env"]))
                    {
                        settings.Env = Configuration["Env"];
                    }
                    SetSettingValues(settings, Configuration);
                    return s;
                });

            var builder = new ContainerBuilder();
            RegisterServices(services, applicationSettings, builder);
            ApplicationContainer = builder.Build();

            return new AutofacServiceProvider(ApplicationContainer);
        }

        protected virtual void SetSettingValues(TSettings source, IConfigurationRoot configuration)
        {
            //if needed TSetting properties may be set
        }

        public virtual void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory,
            IApplicationLifetime appLifetime)
        {
            app.UseMvc();

            var applications = app.ApplicationServices.GetServices<IBrokerApplication>();

            appLifetime.ApplicationStarted.Register(async () =>
            {
                foreach (var application in applications)
                {
                    application.Run();
                }
                
                await Log.WriteMonitorAsync("", "", $"Started");
            });

            appLifetime.ApplicationStopping.Register(() =>
            {
                foreach (var application in applications)
                {
                    application.StopApplication();
                }
            });

            appLifetime.ApplicationStopped.Register(async () =>
            {
                if (Log != null)
                {
                    await Log.WriteMonitorAsync("", "", $"Terminating");
                }
                
                ApplicationContainer.Dispose();
            });
        }

        protected abstract void RegisterCustomServices(IServiceCollection services, ContainerBuilder builder, IReloadingManager<TSettings> settings, ILog log);

        protected virtual ILog CreateLogWithSlack(IServiceCollection services,
            IReloadingManager<TApplicationSettings> settings, CurrentApplicationInfo applicationInfo)
        {
            var logTableName = ApplicationName + applicationInfo.EnvInfo + "Log"; 
            var aggregateLogger = new AggregateLogger();
            var consoleLogger = new LogToConsole();
            
            aggregateLogger.AddLog(consoleLogger);

            #region Logs settings validation

            if (!settings.CurrentValue.MtBrokersLogs.UseSerilog 
                && string.IsNullOrWhiteSpace(settings.CurrentValue.MtBrokersLogs.LogsConnString))
            {
                throw new Exception("Either UseSerilog must be true or LogsConnString must be set");
            }

            #endregion Logs settings validation
            
            #region Slack registration

            IMtSlackNotificationsSender slackService = null;

            if (settings.CurrentValue.SlackNotifications != null)
            {
                var azureQueue = new AzureQueueSettings
                {
                    ConnectionString = settings.CurrentValue.SlackNotifications.AzureQueue.ConnectionString,
                    QueueName = settings.CurrentValue.SlackNotifications.AzureQueue.QueueName
                };

                var commonSlackService =
                    services.UseSlackNotificationsSenderViaAzureQueue(azureQueue, consoleLogger);

                slackService =
                    new MtSlackNotificationsSender(commonSlackService, ApplicationName, Environment.EnvironmentName);
            }
            else
            {
                slackService =
                    new MtSlackNotificationsSenderLogStub(ApplicationName, Environment.EnvironmentName, consoleLogger);
            }
            
            services.AddSingleton<ISlackNotificationsSender>(slackService);
            services.AddSingleton<IMtSlackNotificationsSender>(slackService);

            #endregion Slack registration
            
            if (settings.CurrentValue.MtBrokersLogs.UseSerilog)
            {
                aggregateLogger.AddLog(new SerilogLogger(applicationInfo.GetType().Assembly, Configuration));
            }
            else if (settings.CurrentValue.MtBrokersLogs.StorageMode == StorageMode.SqlServer)
            {
                aggregateLogger.AddLog(new LogToSql(new SqlLogRepository(logTableName,
                    settings.CurrentValue.MtBrokersLogs.LogsConnString)));
            } 
            else if (settings.CurrentValue.MtBrokersLogs.StorageMode == StorageMode.Azure)
            {
                var dbLogConnectionStringManager = settings.Nested(x => x.MtBrokersLogs.LogsConnString);
                var dbLogConnectionString = dbLogConnectionStringManager.CurrentValue;
    
                if (string.IsNullOrEmpty(dbLogConnectionString))
                {
                    consoleLogger.WriteWarningAsync(ApplicationName, 
                        nameof(CreateLogWithSlack), "Table logger is not initialized").Wait();
                    return aggregateLogger;
                }
    
                if (dbLogConnectionString.StartsWith("${") && dbLogConnectionString.EndsWith("}"))
                    throw new InvalidOperationException($"LogsConnString {dbLogConnectionString} is not filled in settings");
    
                // Creating azure storage logger, which logs own messages to console log
                var azureStorageLogger = services.UseLogToAzureStorage(settings.Nested(s => s.MtBrokersLogs.LogsConnString),
                    slackService, logTableName, consoleLogger);
                
                azureStorageLogger.Start();
                
                aggregateLogger.AddLog(azureStorageLogger);
            }

            return aggregateLogger;
        }

        private void RegisterServices(IServiceCollection services, IReloadingManager<TApplicationSettings> applicationSettings,
            ContainerBuilder builder)
        {
            var applicationInfo = new CurrentApplicationInfo(PlatformServices.Default.Application.ApplicationVersion,
                ApplicationName);
            builder.RegisterInstance(applicationInfo).AsSelf().SingleInstance();
            Log = CreateLogWithSlack(services, applicationSettings, applicationInfo);
            builder.RegisterInstance(Log).As<ILog>().SingleInstance();
            builder.RegisterInstance(applicationSettings).AsSelf().SingleInstance();

            var settings = applicationSettings.Nested(s => s.MtBrokerSettings);
            builder.RegisterInstance(settings).AsSelf().SingleInstance();
            builder.RegisterInstance(settings.CurrentValue).AsSelf().SingleInstance();

            RegisterCustomServices(services, builder, settings, Log);
            builder.Populate(services);
        }
    }
}
