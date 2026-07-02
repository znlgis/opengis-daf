using Microsoft.Extensions.Logging;

namespace OpenGisDAF.Infrastructure;

public static class ExceptionHandler
{
    public static void ConfigureGlobalHandler(ILogger? logger = null)
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            logger?.LogCritical(ex, "Unhandled exception: {Message}", ex?.Message);
            Environment.Exit(1);
        };

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            var innerEx = args.Exception.Flatten().InnerException;
            logger?.LogError(args.Exception, "Unobserved task exception: {Message}", innerEx?.Message ?? args.Exception.Message);
            args.SetObserved();
        };
    }
}
