using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OpenGisDAF.Infrastructure;

public class HostBuilder
{
    private readonly IServiceCollection _services = new ServiceCollection();
    private Action<ILoggingBuilder>? _loggingConfig;

    public HostBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        configure(_services);
        return this;
    }

    public HostBuilder ConfigureLogging(Action<ILoggingBuilder> configure)
    {
        _loggingConfig = configure;
        return this;
    }

    public IServiceProvider Build()
    {
        _services.AddLogging(builder =>
        {
            _loggingConfig?.Invoke(builder);
        });

        return _services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }
}
