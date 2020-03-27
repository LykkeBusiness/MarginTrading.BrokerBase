using System;
using System.IO;
using System.Threading;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Lykke.MarginTrading.BrokerBase
{
    public class WebAppProgramBase<TStartup> where TStartup : class
    {
        protected static void RunOnPort(short listeningPort)
        {
            void RunHost()
            {
                Hosting.AppHost = Host.CreateDefaultBuilder()
                        .UseServiceProviderFactory(new AutofacServiceProviderFactory())
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
            };


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