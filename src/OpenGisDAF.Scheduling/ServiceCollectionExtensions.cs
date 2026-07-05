using Microsoft.Extensions.DependencyInjection;
using OpenGisDAF.Core;

namespace OpenGisDAF.Scheduling;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddScheduling(this IServiceCollection services, int maxGlobalConcurrency = 1)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new GlobalConcurrencyController(maxGlobalConcurrency));
        services.AddSingleton<ISchedulingEngine, SerialScheduler>();

        return services;
    }
}
