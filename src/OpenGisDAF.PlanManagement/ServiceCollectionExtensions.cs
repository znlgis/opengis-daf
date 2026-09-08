using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenGisDAF.Core;

namespace OpenGisDAF.PlanManagement;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPlanManagement(
        this IServiceCollection services,
        string? rootPath = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        rootPath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "opengis-daf",
            "plans");

        services.AddSingleton<IPlanSerializer, PlanSerializer>();
        services.AddSingleton<IPlanValidator, PlanValidator>();
        services.AddSingleton<IPlanRepository>(sp =>
        {
            var serializer = sp.GetRequiredService<IPlanSerializer>();
            var logger = sp.GetService<ILogger<PlanRepository>>();
            return new PlanRepository(rootPath, serializer, logger);
        });
        services.AddSingleton<IPlanVersionManager, PlanVersionManager>();
        services.AddSingleton<IPlanManager, PlanManager>();

        return services;
    }
}
