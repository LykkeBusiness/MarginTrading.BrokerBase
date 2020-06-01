using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Lykke.MarginTrading.BrokerBase
{
    [UsedImplicitly]
    public static class ConfigurationBuilderExtensions
    {
        public static IConfigurationBuilder AddDevJson(this IConfigurationBuilder builder, IHostEnvironment env)
        {
            return builder.AddInMemoryCollection(new Dictionary<string, string>
            {
                {"SettingsUrl", Path.Combine(env.ContentRootPath, "appsettings.dev.json")}
            });
        }
    }
}