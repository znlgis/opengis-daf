using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Globalization;

namespace OpenGisDAF.Infrastructure;

public class HostBuilder
{
    private readonly IServiceCollection _services = new ServiceCollection();
    private Action<LoggerConfiguration>? _loggingConfig;
    private bool _built;

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
        if (_built)
            throw new InvalidOperationException("HostBuilder has already been built.");

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);

        _loggingConfig?.Invoke(loggerConfig);

        Log.Logger = loggerConfig.CreateLogger();
        ExceptionHandler.SetLogger(Log.Logger);
        _services.AddLogging(builder => builder.AddSerilog(dispose: true));

        _built = true;
        return _services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    public static void CloseAndFlush()
    {
        Log.CloseAndFlush();
    }
}
