using System;
using System.Collections.Generic;
using System.IO;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Common.Log;
using JetBrains.Annotations;
using Lykke.AzureQueueIntegration;
using Lykke.Common.Api.Contract.Responses;
using Lykke.Common.ApiLibrary.Middleware;
using Lykke.Common.ApiLibrary.Swagger;
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
using Lykke.Snow.Common.Startup;
using Lykke.Snow.Common.Startup.ApiKey;
using Lykke.Snow.Common.Startup.Hosting;
using Lykke.Snow.Common.Startup.Log;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.PlatformAbstractions;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Serialization;
using Serilog.Core;
using IApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;
using IHostingEnvironment = Microsoft.AspNetCore.Hosting.IHostingEnvironment;

namespace Lykke.MarginTrading.BrokerBase
{
    public abstract class  BrokerStartupBase<TApplicationSettings, TSettings>
        where TApplicationSettings : class, IBrokerApplicationSettings<TSettings>
        where TSettings : BrokerSettingsBase
    {
        protected IReloadingManager<TApplicationSettings> _appSettings;
        public IConfigurationRoot Configuration { get; }
        public IHostEnvironment Environment { get; }
        public ILifetimeScope ApplicationContainer { get; private set; }
        public ILog Log { get; private set; }
        protected abstract string ApplicationName { get; }
        protected virtual IEnumerable<ILogEventEnricher> SerilogEventEnrichers => null;

        protected BrokerStartupBase(IHostEnvironment env)
        {
            Configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddDevJson(env)
                .AddSerilogJson(env)
                .AddEnvironmentVariables()
                .Build();

            Environment = env;
        }

        public virtual void ConfigureServices(IServiceCollection services)
        {
            services
                .AddControllers()
                .AddApplicationPart(typeof(Hosting).Assembly)
                .AddNewtonsoftJson(options =>
                {
                    options.SerializerSettings.ContractResolver = new DefaultContractResolver();
                });
            
            services.AddSingleton(Configuration);

            _appSettings = Configuration.LoadSettings<TApplicationSettings>(
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

            var clientSettings = new ClientSettings
            { ApiKey = _appSettings.CurrentValue.MtBrokerSettings.ApiKey };

            services.AddApiKeyAuth(clientSettings);

            services.AddSwaggerGen(options =>
            {
                options.DefaultLykkeConfiguration("v1", ApplicationName + " API");

                if (!string.IsNullOrWhiteSpace(clientSettings.ApiKey))
                {
                    options.AddApiKeyAwareness();
                }
            });
            
            Log = CreateLogWithSlack(
                services,
                _appSettings,
                new CurrentApplicationInfo(
                    PlatformServices.Default.Application.ApplicationVersion,
                    ApplicationName));

            services.AddSingleton<ILoggerFactory>(x => new WebHostLoggerFactory(Log));
        }

        protected virtual void SetSettingValues(TSettings source, IConfigurationRoot configuration)
        {
            //if needed TSetting properties may be set
        }

        [UsedImplicitly]
        public virtual void Configure(IApplicationBuilder app, IHostEnvironment env, IHostApplicationLifetime appLifetime)
        {
            ApplicationContainer = app.ApplicationServices.GetAutofacRoot();
            
#if DEBUG
            app.UseLykkeMiddleware(PlatformServices.Default.Application.ApplicationName, ex => ex.ToString());
#else
            app.UseLykkeMiddleware(PlatformServices.Default.Application.ApplicationName, ex => new ErrorResponse {ErrorMessage = ex.Message});
#endif

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
            app.UseSwagger(c =>
            {
                c.PreSerializeFilters.Add((swagger, httpReq) =>
                    swagger.Servers = new List<OpenApiServer> {
                        new OpenApiServer { Url = $"{httpReq.Scheme}://{httpReq.Host.Value}" }
                    });
            });
            app.UseSwaggerUI(a => a.SwaggerEndpoint("/swagger/v1/swagger.json", $"{ApplicationName} Swagger"));

            var applications = app.ApplicationServices.GetServices<IBrokerApplication>();

            appLifetime.ApplicationStarted.Register(() =>
            {
                foreach (var application in applications)
                {
                    application.Run();
                }

                Hosting.AppHost.WriteLogs(Environment, Log);

                Log.WriteMonitor("", "", $"Started");
            });

            appLifetime.ApplicationStopping.Register(() =>
            {
                foreach (var application in applications)
                {
                    application.StopApplication();
                }
            });

            appLifetime.ApplicationStopped.Register(() =>
            {
                if (Log != null)
                {
                    Log.WriteMonitor("", "", $"Terminating");
                }
                
                ApplicationContainer.Dispose();
            });
        }

        protected abstract void RegisterCustomServices(ContainerBuilder builder, IReloadingManager<TSettings> settings, ILog log);

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
                aggregateLogger.AddLog(new SerilogLogger(applicationInfo.GetType().Assembly, Configuration, SerilogEventEnrichers));
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

        [UsedImplicitly]
        public void ConfigureContainer(ContainerBuilder builder)
        {
            builder.RegisterInstance(this).AsSelf().SingleInstance();
            builder.RegisterInstance(new CurrentApplicationInfo(PlatformServices.Default.Application.ApplicationVersion,
                ApplicationName)).AsSelf().SingleInstance();
            builder.RegisterInstance(Log).As<ILog>().SingleInstance();
            builder.RegisterInstance(_appSettings).AsSelf().SingleInstance();

            var settings = _appSettings.Nested(s => s.MtBrokerSettings);
            builder.RegisterInstance(settings).AsSelf().SingleInstance();
            builder.RegisterInstance(settings.CurrentValue).As<BrokerSettingsBase>().AsSelf().SingleInstance();
            
            builder.RegisterType<RabbitPoisonHandingService>().As<IRabbitPoisonHandingService>().SingleInstance();
            
            RegisterCustomServices(builder, settings, Log);
        }
    }
}
