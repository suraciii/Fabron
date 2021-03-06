using System;
using System.Reflection;
using Orleans.Hosting;

namespace Microsoft.Extensions.Hosting
{
    public static class FabronHostBuilderExtensions
    {
        public static IHostBuilder UseFabron(this IHostBuilder hostBuilder, Assembly commandAssembly, Action<ISiloBuilder>? configureDelegate = null)
        {
            if (configureDelegate == null)
            {
                throw new ArgumentNullException(nameof(configureDelegate));
            }

            hostBuilder.UseOrleans((ctx, siloBuilder) =>
            {
                siloBuilder.AddFabron(commandAssembly);
                configureDelegate?.Invoke(siloBuilder);
            });

            return hostBuilder;
        }
    }
}
