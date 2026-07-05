using Microsoft.Extensions.DependencyInjection;
using OpenGisDAF.Core;

namespace OpenGisDAF.Operators;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOperators(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IOperatorPool>(sp =>
        {
            var pool = new OperatorPool();

            pool.Register(new BufferOperator());
            pool.Register(new ClipOperator());
            pool.Register(new ContainmentCheckOperator());
            pool.Register(new CoordinateTransformOperator());
            pool.Register(new FieldCalculator());
            pool.Register(new GeometryValidityChecker());
            pool.Register(new IntersectCheckOperator());
            pool.Register(new NullValueFiller());
            pool.Register(new AttributeCompletenessChecker());

            return pool;
        });

        services.AddSingleton<IPluginManager, PluginManager>();

        return services;
    }
}
