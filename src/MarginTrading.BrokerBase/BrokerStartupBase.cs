using System.Collections.Generic;
using System.IO;

using Autofac;

using JetBrains.Annotations;

using Lykke.Common.Api.Contract.Responses;
using Lykke.Common.ApiLibrary.Middleware;
using Lykke.Common.ApiLibrary.Swagger;
using Lykke.Logs.Serilog;
using Lykke.MarginTrading.BrokerBase.Extensions;
using Lykke.MarginTrading.BrokerBase.Services;
using Lykke.MarginTrading.BrokerBase.Services.Implementation;
using Lykke.MarginTrading.BrokerBase.Settings;
using Lykke.RabbitMqBroker;
using Lykke.SettingsReader;
using Lykke.Snow.Common.AssemblyLogging;
using Lykke.Snow.Common.Correlation;
using Lykke.Snow.Common.Correlation.Cqrs;
using Lykke.Snow.Common.Correlation.Http;
using Lykke.Snow.Common.Correlation.RabbitMq;
using Lykke.Snow.Common.Startup;
using Lykke.Snow.Common.Startup.ApiKey;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.PlatformAbstractions;
using Microsoft.OpenApi.Models;

using Newtonsoft.Json.Serialization;

namespace Lykke.MarginTrading.BrokerBase
{
    public abstract class BrokerStartupBase<TApplicationSettings, TSettings>
        where TApplicationSettings : class, IBrokerApplicationSettings<TSettings>
        where TSettings : BrokerSettingsBase
    {
        protected IReloadingManager<TApplicationSettings> _appSettings;
        public IConfiguration Configuration { get; }
        public IHostEnvironment Environment { get; }
        protected abstract string ApplicationName { get; }

        protected BrokerStartupBase(IHostEnvironment env, IConfiguration configuration)
        {
            Environment = env;
            Configuration = configuration;
        }

        public virtual void ConfigureServices(IServiceCollection services)
        {
            services.AddAssemblyLogger();
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

            var correlationContextAccessor = new CorrelationContextAccessor();
            services.AddSingleton(correlationContextAccessor);
            services.AddSingleton<RabbitMqCorrelationManager>();
            services.AddSingleton<CqrsCorrelationManager>();
            services.AddTransient<HttpCorrelationHandler>();
        }

        protected virtual void SetSettingValues(TSettings source, IConfiguration configuration)
        {
            //if needed TSetting properties may be set
        }

        public virtual void Configure(IApplicationBuilder app, IHostEnvironment env,
            IHostApplicationLifetime appLifetime)
        {
            app.UseCorrelation();

#if DEBUG
            app.UseLykkeMiddleware(PlatformServices.Default.Application.ApplicationName, ex => ex.ToString(),
                logClientErrors: false, useStandardLogger: true);
#else
            app.UseLykkeMiddleware(PlatformServices.Default.Application.ApplicationName,
                ex => new ErrorResponse { ErrorMessage = ex.Message }, logClientErrors: false, useStandardLogger: true);
#endif

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
            app.UseSwagger(c =>
            {
                c.PreSerializeFilters.Add((swagger, httpReq) =>
                    swagger.Servers = new List<OpenApiServer>
                    {
                        new OpenApiServer {Url = $"{httpReq.Scheme}://{httpReq.Host.Value}"}
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
            });

            appLifetime.ApplicationStopping.Register(() =>
            {
                foreach (var application in applications)
                {
                    application.StopApplication();
                }
            });
        }

        protected abstract void RegisterCustomServices(ContainerBuilder builder, IReloadingManager<TSettings> settings);

        [UsedImplicitly]
        public void ConfigureContainer(ContainerBuilder builder)
        {
            builder.RegisterInstance(this).AsSelf().SingleInstance();
            builder.RegisterInstance(new CurrentApplicationInfo(PlatformServices.Default.Application.ApplicationVersion,
                ApplicationName)).AsSelf().SingleInstance();
            builder.RegisterInstance(_appSettings).AsSelf().SingleInstance();

            var settings = _appSettings.Nested(s => s.MtBrokerSettings);
            builder.RegisterInstance(settings).AsSelf().SingleInstance();
            builder.RegisterInstance(settings.CurrentValue).As<BrokerSettingsBase>().AsSelf().SingleInstance();

            builder.AddRabbitMqConnectionProvider();
            builder
                .RegisterType<RabbitMqPoisonQueueHandler>()
                .As<IRabbitMqPoisonQueueHandler>()
                .SingleInstance();
            builder.RegisterDecorator<ParallelExecutionGuardPoisonQueueDecorator, IRabbitMqPoisonQueueHandler>();

            RegisterCustomServices(builder, settings);
        }
    }
}