using Microsoft.Extensions.DependencyInjection;
using OpenGisDAF.Core;

namespace OpenGisDAF.Execution;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddExecution(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IResultCache, ResultCache>();
        services.AddSingleton<SourceFactory>();
        services.AddSingleton<SinkFactory>();
        services.AddSingleton<IExecutionEngine, ExecutionEngine>();

        // TODO: Register after Phase E (QC components)
        // services.AddSingleton<QcResultCollector>();
        // services.AddSingleton<QualityCalculator>();
        // services.AddSingleton<QualityReportGenerator>();

        return services;
    }
}
