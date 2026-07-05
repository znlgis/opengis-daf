using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Globalization;

namespace OpenGisDAF.Infrastructure;

public class HostBuilder
{
    private readonly IServiceCollection _services = new ServiceCollection();
    private Action<LoggerConfiguration>? _loggingConfig;

    public HostBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        configure(_services);
        return this;
    }

    public HostBuilder ConfigureLogging(Action<LoggerConfiguration> configure)
    {
        _loggingConfig = configure;
        return this;
    }

    public IServiceProvider Build()
    {
        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);

        _loggingConfig?.Invoke(loggerConfig);

        Log.Logger = loggerConfig.CreateLogger();
        _services.AddLogging(builder => builder.AddSerilog(dispose: true));

        return _services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }
}
