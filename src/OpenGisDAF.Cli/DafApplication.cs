using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenGisDAF.Core;
using OpenGisDAF.Execution;
using OpenGisDAF.Infrastructure;
using OpenGisDAF.Operators;
using OpenGisDAF.PlanManagement;
using OpenGisDAF.Scheduling;

namespace OpenGisDAF.Cli;

public sealed class DafApplication
{
    private readonly IServiceProvider _services;

    public DafApplication()
    {
        var builder = new HostBuilder();

        builder.ConfigureServices(services =>
        {
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(JsonConfiguration.Create());

            services.AddPlanManagement();
            services.AddOperators();
            services.AddExecution();
            services.AddScheduling();
        });

        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddConsole();
        });

        _services = builder.Build();
    }

    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var commandArgs = args.Skip(1).ToArray();

        try
        {
            return command switch
            {
                "run" => await RunPlanAsync(commandArgs),
                "validate" => await ValidatePlanAsync(commandArgs),
                "operator" => await OperatorCommandAsync(commandArgs),
                "plan" => await PlanCommandAsync(commandArgs),
                "help" or "--help" or "-h" => PrintUsageAndReturn(),
                _ => UnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            return 1;
        }
    }

    private async Task<int> RunPlanAsync(string[] args)
    {
        if (args.Length < 2 || args[0] != "--plan")
        {
            Console.Error.WriteLine("用法: daf run --plan <path>");
            return 1;
        }

        var planPath = args[1];
        var json = await File.ReadAllTextAsync(planPath);
        var serializer = _services.GetRequiredService<IPlanSerializer>();
        var plan = serializer.Deserialize(json);

        var validator = _services.GetRequiredService<IPlanValidator>();
        var operatorPool = _services.GetRequiredService<IOperatorPool>();
        var validation = validator.Validate(plan, operatorPool);
        if (!validation.IsValid)
        {
            Console.Error.WriteLine("方案校验失败:");
            foreach (var err in validation.Errors)
                Console.Error.WriteLine($"  [{err.Code}] {err.Message} ({err.Location})");
            return 1;
        }

        foreach (var warn in validation.Warnings)
            Console.WriteLine($"  警告: [{warn.Code}] {warn.Message}");

        var scheduler = _services.GetRequiredService<ISchedulingEngine>();
        var logger = _services.GetRequiredService<ILogger<DafApplication>>();

#pragma warning disable CA1848, CA1873
        logger.LogInformation("开始执行方案: {PlanName} (ID: {PlanId})", plan.Name, plan.Id);
#pragma warning restore CA1848, CA1873

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("正在取消...");
        };

        var stats = await scheduler.ExecuteAsync(plan, cts.Token);

        Console.WriteLine();
        Console.WriteLine("=== 执行完成 ===");
        Console.WriteLine($"总耗时: {stats.TotalElapsed.TotalSeconds:F2}s");
        foreach (var itemStat in stats.ItemStats)
        {
            var status = itemStat.FailedCount > 0 ? "失败" : "成功";
            Console.WriteLine($"  {itemStat.OperatorId} ({itemStat.ItemId}): {status} ({itemStat.Elapsed.TotalSeconds:F2}s)");
        }

        if (stats.QcStats is not null)
        {
            Console.WriteLine();
            Console.WriteLine($"质检问题总数: {stats.QcStats.TotalIssues}");
            foreach (var (severity, count) in stats.QcStats.IssuesBySeverity)
                Console.WriteLine($"  {severity}: {count}");

            var reportPath = Path.ChangeExtension(planPath, ".qc-report.json");
            await GenerateReportIfQcMode(plan, stats, reportPath);
        }

        return 0;
    }

    private static async Task GenerateReportIfQcMode(AnalysisPlan plan, PlanExecutionStatistics stats, string reportPath)
    {
        var hasQcMode = plan.Items.Any(i => i.ExecutionPolicy.QcMode);
        if (!hasQcMode) return;

        var issuesByItem = stats.Issues
            .GroupBy(i => i.ItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var report = QualityReportGenerator.Generate(
            stats.Issues, issuesByItem, plan, Guid.NewGuid().ToString("N"));

        await QualityReportGenerator.SaveAsync(report, reportPath);
        Console.WriteLine($"质检报告已保存: {reportPath}");
        Console.WriteLine($"质量评分: {report.TotalScore}/100");
    }

    private async Task<int> ValidatePlanAsync(string[] args)
    {
        if (args.Length < 2 || args[0] != "--plan")
        {
            Console.Error.WriteLine("用法: daf validate --plan <path>");
            return 1;
        }

        var planPath = args[1];
        var json = await File.ReadAllTextAsync(planPath);
        var serializer = _services.GetRequiredService<IPlanSerializer>();
        var plan = serializer.Deserialize(json);

        var validator = _services.GetRequiredService<IPlanValidator>();
        var operatorPool = _services.GetRequiredService<IOperatorPool>();
        var validation = validator.Validate(plan, operatorPool);

        Console.WriteLine($"方案 '{plan.Name}' 校验结果: {(validation.IsValid ? "通过" : "失败")}");
        Console.WriteLine($"  错误: {validation.Errors.Count}");
        Console.WriteLine($"  警告: {validation.Warnings.Count}");

        foreach (var err in validation.Errors)
            Console.WriteLine($"  [错误] [{err.Code}] {err.Message} ({err.Location})");

        foreach (var warn in validation.Warnings)
            Console.WriteLine($"  [警告] [{warn.Code}] {warn.Message} ({warn.Location})");

        return validation.IsValid ? 0 : 1;
    }

    private async Task<int> OperatorCommandAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("用法: daf operator <list|import> [...]");
            return 1;
        }

        return args[0].ToLowerInvariant() switch
        {
            "list" => await OperatorListAsync(args.Skip(1).ToArray()),
            "import" => await OperatorImportAsync(args.Skip(1).ToArray()),
            _ => UnknownCommand($"operator {args[0]}")
        };
    }

    private Task<int> OperatorListAsync(string[] args)
    {
        var pool = _services.GetRequiredService<IOperatorPool>();
        string? category = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--category" && i + 1 < args.Length)
                category = args[++i];
        }

        var operators = category is not null
            ? pool.GetByCategory(category)
            : pool.GetAll();

        Console.WriteLine($"已注册算子 ({operators.Count}):");
        foreach (var op in operators)
        {
            Console.WriteLine($"  {op.Metadata.Id,-40} [{op.Metadata.Category}] {op.Metadata.Version}");
            Console.WriteLine($"    {op.Metadata.Description}");
        }

        return Task.FromResult(0);
    }

    private Task<int> OperatorImportAsync(string[] args)
    {
        if (args.Length < 2 || args[0] != "--dll")
        {
            Console.Error.WriteLine("用法: daf operator import --dll <path>");
            return Task.FromResult(1);
        }

        var dllPath = args[1];
        if (!File.Exists(dllPath))
        {
            Console.Error.WriteLine($"DLL 文件不存在: {dllPath}");
            return Task.FromResult(1);
        }

        var pluginManager = _services.GetRequiredService<IPluginManager>();
        pluginManager.ImportPlugin(dllPath);

        Console.WriteLine($"成功导入插件: {dllPath}");
        return Task.FromResult(0);
    }

    private async Task<int> PlanCommandAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("用法: daf plan <list|create|copy|export> [...]");
            return 1;
        }

        return args[0].ToLowerInvariant() switch
        {
            "list" => await PlanListAsync(args.Skip(1).ToArray()),
            "create" => await PlanCreateAsync(args.Skip(1).ToArray()),
            "copy" => await PlanCopyAsync(args.Skip(1).ToArray()),
            "export" => await PlanExportAsync(args.Skip(1).ToArray()),
            _ => UnknownCommand($"plan {args[0]}")
        };
    }

    private async Task<int> PlanListAsync(string[] args)
    {
        string? group = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--group" && i + 1 < args.Length)
                group = args[++i];
        }

        var repo = _services.GetRequiredService<IPlanRepository>();
        var plans = await repo.ListAsync(group);

        Console.WriteLine($"方案列表 ({plans.Count}):");
        foreach (var p in plans)
        {
            Console.WriteLine($"  [{p.Group}] {p.Name} v{p.Version} ({p.ItemCount} 项, 更新于 {p.LastModified:yyyy-MM-dd HH:mm})");
        }

        return 0;
    }

    private async Task<int> PlanCreateAsync(string[] args)
    {
        string? name = null;
        string? group = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--name" && i + 1 < args.Length)
                name = args[++i];
            else if (args[i] == "--group" && i + 1 < args.Length)
                group = args[++i];
        }

        if (name is null)
        {
            Console.Error.WriteLine("用法: daf plan create --name <name> [--group <group>]");
            return 1;
        }

        var plan = new AnalysisPlan
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = name,
            Version = "1.0.0",
            Group = group ?? "default"
        };

        var manager = _services.GetRequiredService<IPlanManager>();
        await manager.CreateAsync(plan);

        Console.WriteLine($"方案已创建: [{plan.Group}] {plan.Name} ({plan.Id})");
        return 0;
    }

    private async Task<int> PlanCopyAsync(string[] args)
    {
        string? source = null;
        string? target = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--source" && i + 1 < args.Length)
                source = args[++i];
            else if (args[i] == "--target" && i + 1 < args.Length)
                target = args[++i];
        }

        if (source is null || target is null)
        {
            Console.Error.WriteLine("用法: daf plan copy --source <group/name> --target <group/name>");
            return 1;
        }

        var sourceParts = source.Split('/');
        var targetParts = target.Split('/');

        if (sourceParts.Length != 2 || targetParts.Length != 2)
        {
            Console.Error.WriteLine("格式: group/name (如 'default/myplan')");
            return 1;
        }

        var repo = _services.GetRequiredService<IPlanRepository>();
        var manager = _services.GetRequiredService<IPlanManager>();

        var sourcePlans = await repo.ListAsync(sourceParts[0]);
        var sourceSummary = sourcePlans.FirstOrDefault(p =>
            string.Equals(p.Name, sourceParts[1], StringComparison.OrdinalIgnoreCase));

        if (sourceSummary is null)
        {
            Console.Error.WriteLine($"未找到源方案: 组='{sourceParts[0]}', 名称='{sourceParts[1]}'");
            return 1;
        }

        var sourcePlan = await manager.LoadAsync(sourceSummary.Id);
        if (sourcePlan is null)
        {
            Console.Error.WriteLine($"无法加载源方案: {sourceSummary.Id}");
            return 1;
        }

        var newPlan = new AnalysisPlan
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = targetParts[1],
            Version = "1.0.0",
            Group = targetParts[0],
            Items = sourcePlan.Items,
            SubPlans = sourcePlan.SubPlans,
            ExecutionPolicy = sourcePlan.ExecutionPolicy,
        };

        await manager.CreateAsync(newPlan);
        Console.WriteLine($"方案已复制: [{newPlan.Group}] {newPlan.Name} ({newPlan.Id})");
        return 0;
    }

    private async Task<int> PlanExportAsync(string[] args)
    {
        string? planId = null;
        string? output = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--plan" && i + 1 < args.Length)
                planId = args[++i];
            else if (args[i] == "--output" && i + 1 < args.Length)
                output = args[++i];
        }

        if (planId is null)
        {
            Console.Error.WriteLine("用法: daf plan export --plan <group/name> --output <path>");
            return 1;
        }

        output ??= $"{planId.Replace('/', '_')}.export.json";
        var parts = planId.Split('/');

        if (parts.Length != 2)
        {
            Console.Error.WriteLine("格式: group/name (如 'default/myplan')");
            return 1;
        }

        var repo = _services.GetRequiredService<IPlanRepository>();
        var plans = await repo.ListAsync(parts[0]);
        var summary = plans.FirstOrDefault(p =>
            string.Equals(p.Name, parts[1], StringComparison.OrdinalIgnoreCase));

        if (summary is null)
        {
            Console.Error.WriteLine($"未找到方案: 组='{parts[0]}', 名称='{parts[1]}'");
            return 1;
        }

        var manager = _services.GetRequiredService<IPlanManager>();
        var json = await manager.ExportAsync(summary.Id);
        await File.WriteAllTextAsync(output, json);

        Console.WriteLine($"方案已导出: {output}");
        return 0;
    }

    private static int PrintUsageAndReturn()
    {
        PrintUsage();
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"未知命令: {command}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("OpenGIS DAF — GIS 数据分析与质检框架");
        Console.WriteLine();
        Console.WriteLine("用法: daf <command> [options]");
        Console.WriteLine();
        Console.WriteLine("命令:");
        Console.WriteLine("  run --plan <path>              执行方案");
        Console.WriteLine("  validate --plan <path>         校验方案（Dry-Run）");
        Console.WriteLine("  operator list [--category <c>] 列出算子");
        Console.WriteLine("  operator import --dll <path>   导入算子 DLL");
        Console.WriteLine("  plan list [--group <g>]        列出方案");
        Console.WriteLine("  plan create --name <n> [--group <g>]  创建方案");
        Console.WriteLine("  plan copy --source <g/n> --target <g/n>  复制方案");
        Console.WriteLine("  plan export --plan <g/n> [--output <p>]  导出方案");
    }
}
