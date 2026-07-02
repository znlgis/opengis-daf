using Microsoft.Extensions.Logging;

namespace OpenGisDAF.Infrastructure;

public static class ExceptionHandler
{
    private static bool _isConfigured;
    private static readonly object _lock = new();

    public static void ConfigureGlobalHandler(ILogger? logger = null)
    {
        lock (_lock)
        {
            if (_isConfigured) return;
            _isConfigured = true;
        }

        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            var message = ex?.Message ?? args.ExceptionObject?.ToString() ?? "Unknown unhandled exception";
            if (logger != null)
            {
                logger.LogCritical(ex, "Unhandled exception: {Message}", message);
            }
            else
            {
                Console.Error.WriteLine($"[Critical] Unhandled exception: {message}");
                if (ex != null) Console.Error.WriteLine(ex.ToString());
            }
        };

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            var innerEx = args.Exception.Flatten().InnerException;
            var message = innerEx?.Message ?? args.Exception.Message;
            if (logger != null)
            {
                logger.LogError(args.Exception, "Unobserved task exception: {Message}", message);
            }
            else
            {
                Console.Error.WriteLine($"[Error] Unobserved task exception: {message}");
                Console.Error.WriteLine(args.Exception.ToString());
            }
            args.SetObserved();
        };
    }
}
