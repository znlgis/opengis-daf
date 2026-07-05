using Microsoft.Extensions.DependencyInjection;
using OpenGisDAF.Adapters.Utilities;
using OpenGisDAF.Core;

namespace OpenGisDAF.Execution;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddExecution(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IResultCache, ResultCache>();
        services.AddSingleton<IConnectionEncryption, DpapiConnectionEncryption>();
        services.AddSingleton<SourceFactory>();
        services.AddSingleton<SinkFactory>();
        services.AddSingleton<IExecutionEngine, ExecutionEngine>();

        services.AddSingleton<QcResultCollector>();
        services.AddSingleton<QualityCalculator>();
        services.AddSingleton<QualityReportGenerator>();

        return services;
    }
}
