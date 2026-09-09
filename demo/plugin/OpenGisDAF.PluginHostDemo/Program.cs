using System.Text;
using Microsoft.Extensions.DependencyInjection;
using OpenGisDAF.Core;
using OpenGisDAF.Execution;
using OpenGisDAF.Infrastructure;
using OpenGisDAF.Operators;
using OpenGisDAF.PlanManagement;
using OpenGisDAF.Scheduling;

// 插件宿主演示：同进程完成"导入插件算子 → 校验方案 → 执行方案"。
//
// 用法（在 demo 目录下）：
//   dotnet plugin/OpenGisDAF.PluginHostDemo/bin/Release/net10.0/OpenGisDAF.PluginHostDemo.dll
//       --plugin plugin/OpenGisDAF.SamplePlugin/bin/Release/net10.0/OpenGisDAF.SamplePlugin.dll
//       --plan   plugin/plans/road-length.json
//
// 与 CLI 的区别：CLI 的 `daf operator import` 加载的算子只在当前 CLI 进程内有效，
// `daf run` 是新进程无法使用；本宿主在同一进程内导入并执行，插件算子真实生效。

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0)
{
    PrintUsage();
    return 0;
}

string? pluginPath = null;
string? planPath = null;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--plugin") pluginPath = args[i + 1];
    else if (args[i] == "--plan") planPath = args[i + 1];
}

if (pluginPath is null || planPath is null)
{
    PrintUsage();
    return 1;
}

return await RunAsync(pluginPath, planPath);

static async Task<int> RunAsync(string pluginPath, string planPath)
{
    if (!File.Exists(pluginPath))
    {
        Console.Error.WriteLine($"插件 DLL 不存在: {pluginPath}");
        return 1;
    }
    if (!File.Exists(planPath))
    {
        Console.Error.WriteLine($"方案文件不存在: {planPath}");
        return 1;
    }

    // 1. 搭建与 CLI 相同的服务容器（计划管理 / 算子 / 执行 / 调度）
    var builder = new HostBuilder();
    builder.ConfigureServices(services =>
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(JsonConfiguration.Create());
        services.AddPlanManagement();
        services.AddOperators();
        services.AddExecution();
        services.AddScheduling();
    });
    builder.ConfigureLogging(config => config.MinimumLevel.Information());
    var services = builder.Build();

    try
    {
        // 2. 同进程导入插件（关键步骤）
        var pluginManager = services.GetRequiredService<IPluginManager>();
        var imported = pluginManager.ImportPlugin(pluginPath);
        Console.WriteLine($"已导入插件 {Path.GetFileName(pluginPath)}，注册算子 {imported.Count} 个：");
        foreach (var op in imported)
            Console.WriteLine($"  {op.Metadata.Id} [{op.Metadata.Category}] - {op.Metadata.Description}");

        // 3. 加载并校验方案
        var serializer = services.GetRequiredService<IPlanSerializer>();
        var plan = serializer.Deserialize(await File.ReadAllTextAsync(planPath));
        if (plan.Name is null || plan.Id is null)
        {
            Console.Error.WriteLine("方案文件无效: 缺少必要字段 (name/id)");
            return 1;
        }

        var validator = services.GetRequiredService<IPlanValidator>();
        var operatorPool = services.GetRequiredService<IOperatorPool>();
        var validation = await validator.ValidateAsync(plan, operatorPool);
        if (!validation.IsValid)
        {
            Console.Error.WriteLine("方案校验失败:");
            foreach (var err in validation.Errors)
                Console.Error.WriteLine($"  [{err.Code}] {err.Message} ({err.Location})");
            return 1;
        }
        foreach (var warn in validation.Warnings)
            Console.WriteLine($"  警告: [{warn.Code}] {warn.Message}");

        // 4. 执行方案（插件算子与内置算子共同参与 DAG）
        var scheduler = services.GetRequiredService<ISchedulingEngine>();
        Console.WriteLine();
        Console.WriteLine($"开始执行方案: {plan.Name} (ID: {plan.Id})");
        var stats = await scheduler.ExecuteAsync(plan, CancellationToken.None);

        // 5. 输出执行统计（与 CLI run 一致的判定规则：失败或跳过 → 退出码 1）
        Console.WriteLine();
        Console.WriteLine("=== 执行完成 ===");
        Console.WriteLine($"总耗时: {stats.TotalElapsed.TotalSeconds:F2}s");
        var hasFailure = false;
        foreach (var itemStat in stats.ItemStats)
        {
            var status = itemStat.FailedCount > 0 ? "失败"
                : itemStat.SkippedCount > 0 ? "跳过"
                : "成功";
            if (itemStat.FailedCount > 0 || itemStat.SkippedCount > 0)
                hasFailure = true;
            Console.WriteLine($"  {itemStat.OperatorId} ({itemStat.ItemId}): {status} ({itemStat.Elapsed.TotalSeconds:F2}s)");
        }

        if (stats.QcStats is not null)
        {
            Console.WriteLine($"质检问题总数: {stats.QcStats.TotalIssues}");
        }

        return hasFailure ? 1 : 0;
    }
    finally
    {
        if (services is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else if (services is IDisposable disposable)
            disposable.Dispose();
    }
}

static void PrintUsage()
{
    Console.WriteLine("用法: OpenGisDAF.PluginHostDemo --plugin <算子插件.dll> --plan <方案.json>");
}
