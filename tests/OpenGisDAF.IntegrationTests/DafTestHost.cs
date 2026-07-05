using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenGisDAF.Core;
using OpenGisDAF.Execution;
using OpenGisDAF.Infrastructure;
using OpenGisDAF.Operators;
using OpenGisDAF.PlanManagement;
using OpenGisDAF.Scheduling;

namespace OpenGisDAF.IntegrationTests;

public sealed class DafTestHost : IDisposable
{
    public IServiceProvider Services { get; }
    public string TempDir { get; }
    public string TestDataDir { get; }

    public DafTestHost()
    {
        TempDir = Path.Combine(Path.GetTempPath(), $"daf_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(TempDir);

        TestDataDir = Path.Combine(AppContext.BaseDirectory, "TestData");

        var builder = new HostBuilder();

        builder.ConfigureServices(services =>
        {
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(JsonConfiguration.Create());
            services.AddPlanManagement(TempDir);
            services.AddOperators();
            services.AddExecution();
            services.AddScheduling();
        });

        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddConsole();
        });

        Services = builder.Build();
    }

    public string GetTestDataPath(string filename) =>
        Path.Combine(TestDataDir, filename);

    public void Dispose()
    {
        if (Directory.Exists(TempDir))
            Directory.Delete(TempDir, recursive: true);
    }
}
