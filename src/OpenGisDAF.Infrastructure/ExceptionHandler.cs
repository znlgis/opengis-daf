using Microsoft.Extensions.Logging;

namespace OpenGisDAF.Infrastructure;

public static class ExceptionHandler
{
    public static void ConfigureGlobalHandler(ILogger? logger = null)
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            logger?.LogCritical(ex, "未处理的异常: {Message}", ex?.Message);
            Environment.Exit(1);
        };

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            logger?.LogError(args.Exception, "未观察到的任务异常: {Message}", args.Exception.Message);
            args.SetObserved();
        };
    }
}
