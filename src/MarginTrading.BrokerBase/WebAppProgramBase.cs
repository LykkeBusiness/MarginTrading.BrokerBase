using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Autofac.Extensions.DependencyInjection;
using Lykke.Logs.Serilog;
using Lykke.SettingsReader.ConfigurationProvider;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Lykke.MarginTrading.BrokerBase
{
    public class WebAppProgramBase<TStartup> where TStartup : class
    {
        protected static void RunOnPort(short listeningPort, bool useSettingsService = false)
        {
            void RunHost()
            {
                Hosting.AppHost = Host.CreateDefaultBuilder()
                    .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                    .ConfigureAppConfiguration((context, builder) =>
                    {
                        var configurationBuilder = new ConfigurationBuilder()
                            .SetBasePath(Directory.GetCurrentDirectory())
                            .AddDevJson(context.HostingEnvironment)
                            .AddSerilogJson(context.HostingEnvironment)
                            .AddEnvironmentVariables();

                        if (useSettingsService)
                            configurationBuilder.AddHttpSourceConfiguration();

                        builder.AddConfiguration(configurationBuilder.Build());
                    })
                    .UseSerilog((context, cfg) =>
                    {
                        var a = Assembly.GetEntryAssembly();
                        var title =
                            a.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ??
                            string.Empty;
                        var version =
                            a.GetCustomAttribute<
                                    AssemblyInformationalVersionAttribute>()
                                ?.InformationalVersion ?? string.Empty;
                        var environment =
                            Environment.GetEnvironmentVariable(
                                "ASPNETCORE_ENVIRONMENT") ?? string.Empty;

                        cfg.ReadFrom.Configuration(context.Configuration)
                            .Enrich.WithProperty("Application", title)
                            .Enrich.WithProperty("Version", version)
                            .Enrich.WithProperty("Environment", environment);
                    })
                    .ConfigureWebHostDefaults(webBuilder =>
                    {
                        webBuilder.ConfigureKestrel(serverOptions =>
                            {
                                // Set properties and call methods on options
                            })
                            .UseStartup<TStartup>()
                            .UseUrls("http://*:" + listeningPort)
                            .UseContentRoot(Directory.GetCurrentDirectory());
                    })
                    .Build();

                Hosting.AppHost.Run();
            }

            StartWithRetries(RunHost);
        }

        private static void StartWithRetries(Action runHost)
        {
            var restartAttempsLeft = 5;
            while (restartAttempsLeft > 0)
            {
                try
                {
                    runHost();
                    restartAttempsLeft = 0;
                }
                catch (Exception e)
                {
                    Console.WriteLine(
                        $"Error: {e.Message}{Environment.NewLine}{e.StackTrace}{Environment.NewLine}Restarting (attempts left: {restartAttempsLeft})...");
                    restartAttempsLeft--;
                    Thread.Sleep(10000);
                }
            }
        }
    }
}