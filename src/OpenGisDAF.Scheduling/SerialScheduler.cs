using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenGisDAF.Adapters;
using OpenGisDAF.Core;
using OpenGisDAF.Execution;
using ExecutionContext = OpenGisDAF.Core.ExecutionContext;

namespace OpenGisDAF.Scheduling;

public sealed partial class SerialScheduler : ISchedulingEngine
{
    private readonly IExecutionEngine _executionEngine;
    private readonly SourceFactory _sourceFactory;
    private readonly SinkFactory _sinkFactory;
    private readonly IResultCache _resultCache;
    private readonly GlobalConcurrencyController _concurrencyController;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SerialScheduler> _logger;

    public SerialScheduler(
        IExecutionEngine executionEngine,
        SourceFactory sourceFactory,
        SinkFactory sinkFactory,
        IResultCache resultCache,
        GlobalConcurrencyController concurrencyController,
        IServiceProvider serviceProvider,
        ILogger<SerialScheduler> logger)
    {
        _executionEngine = executionEngine;
        _sourceFactory = sourceFactory;
        _sinkFactory = sinkFactory;
        _resultCache = resultCache;
        _concurrencyController = concurrencyController;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<PlanExecutionStatistics> ExecuteAsync(
        AnalysisPlan plan,
        CancellationToken cancellationToken = default)
    {
        var planSw = Stopwatch.StartNew();
        var executionId = Guid.NewGuid().ToString("N");
        var itemStats = new List<PerItemStats>();
        var allIssues = new List<IssueRecord>();
        var startTime = DateTimeOffset.UtcNow;

        var dagResult = DagBuilder.Build(plan.Items);
        if (!dagResult.IsValid)
        {
            Log.DagInvalid(_logger, dagResult.ErrorMessage ?? "未知错误");
            return new PlanExecutionStatistics
            {
                StartTime = startTime,
                EndTime = DateTimeOffset.UtcNow,
                TotalElapsed = planSw.Elapsed,
                ItemStats = itemStats
            };
        }

        var sortResult = TopologicalSorter.Sort(plan.Items, dagResult.Adjacency!);
        if (!sortResult.IsComplete)
        {
            Log.SortIncomplete(_logger);
            return new PlanExecutionStatistics
            {
                StartTime = startTime,
                EndTime = DateTimeOffset.UtcNow,
                TotalElapsed = planSw.Elapsed,
                ItemStats = itemStats
            };
        }

        var externalSources = new Dictionary<string, IFeatureSource>();
        var failedItemIds = new HashSet<string>();

        try
        {
            foreach (var item in sortResult.OrderedItems)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // When ContinueIndependent, skip items whose upstream dependencies failed
                if (plan.ExecutionPolicy.FailurePolicy == FailurePolicy.ContinueIndependent
                    && HasFailedUpstream(item, failedItemIds))
                {
                    itemStats.Add(new PerItemStats
                    {
                        ItemId = item.Id,
                        OperatorId = item.OperatorId,
                        Elapsed = TimeSpan.Zero,
                        SkippedCount = 1
                    });
                    Log.ItemSkippedUpstreamFailed(_logger, item.Id);
                    continue;
                }

                await _concurrencyController.WaitAsync(cancellationToken);
                try
                {
                    Log.ItemExecutionStart(_logger, item.Id, item.OperatorId);
                    var itemSw = Stopwatch.StartNew();

                    var resolvedInputs = await ResolveInputsAsync(item, executionId, externalSources);

                    // Execute
                    var context = new ExecutionContext(
                        plan.Id, executionId, _resultCache, _logger, _serviceProvider,
                        new PlanExecutionStatistics { StartTime = startTime },
                        currentItemId: item.Id);
                    var result = await _executionEngine.ExecuteItemAsync(
                        item, resolvedInputs, context, cancellationToken);

                    itemSw.Stop();

                    // Handle outputs
                    if (result.Status == ExecutionStatus.Success)
                    {
                        await HandleOutputsAsync(item, result, executionId, cancellationToken);
                        await CacheOutputsForDownstream(item.Id, result, executionId);
                        CollectIssues(result, allIssues);

                        itemStats.Add(new PerItemStats
                        {
                            ItemId = item.Id,
                            OperatorId = item.OperatorId,
                            Elapsed = itemSw.Elapsed,
                            SuccessCount = 1
                        });
                    }
                    else
                    {
                        Log.ItemExecutionFailed(
                            _logger, item.Id, result.ErrorCode ?? "UNKNOWN", result.ErrorMessage ?? "无错误信息");
                        failedItemIds.Add(item.Id);

                        if (plan.ExecutionPolicy.FailurePolicy == FailurePolicy.StopOnAny)
                        {
                            Log.StopOnAny(_logger);
                            itemStats.Add(new PerItemStats
                            {
                                ItemId = item.Id,
                                OperatorId = item.OperatorId,
                                Elapsed = itemSw.Elapsed,
                                FailedCount = 1
                            });

                            // Track skipped items
                            var foundCurrent = false;
                            foreach (var remaining in sortResult.OrderedItems)
                            {
                                if (!foundCurrent)
                                {
                                    if (remaining.Id == item.Id)
                                        foundCurrent = true;
                                    continue;
                                }
                                itemStats.Add(new PerItemStats
                                {
                                    ItemId = remaining.Id,
                                    OperatorId = remaining.OperatorId,
                                    Elapsed = TimeSpan.Zero,
                                    SkippedCount = 1
                                });
                            }
                            break;
                        }

                        itemStats.Add(new PerItemStats
                        {
                            ItemId = item.Id,
                            OperatorId = item.OperatorId,
                            Elapsed = itemSw.Elapsed,
                            FailedCount = 1
                        });
                    }
                }
                finally
                {
                    _concurrencyController.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.PlanExecutionAborted(_logger, ex);
            planSw.Stop();
            return new PlanExecutionStatistics
            {
                StartTime = startTime,
                EndTime = DateTimeOffset.UtcNow,
                TotalElapsed = planSw.Elapsed,
                ItemStats = itemStats
            };
        }
        finally
        {
            // Cleanup external sources
            foreach (var source in externalSources.Values)
            {
                await source.DisposeAsync();
            }
        }

        await _resultCache.ClearAsync(cancellationToken);

        planSw.Stop();
        var endTime = DateTimeOffset.UtcNow;

        var qcStats = allIssues.Count > 0
            ? new QcStatistics
            {
                TotalIssues = allIssues.Count,
                IssuesBySeverity = allIssues
                    .GroupBy(i => i.Severity.ToString())
                    .ToDictionary(g => g.Key, g => g.Count()),
                IssuesByCategory = allIssues
                    .GroupBy(i => i.IssueType)
                    .ToDictionary(g => g.Key, g => g.Count())
            }
            : null;

        return new PlanExecutionStatistics
        {
            ExecutionId = executionId,
            StartTime = startTime,
            EndTime = endTime,
            TotalElapsed = planSw.Elapsed,
            ItemStats = itemStats,
            QcStats = qcStats,
            Issues = allIssues
        };
    }

    private async Task<IReadOnlyDictionary<string, IFeatureSource>> ResolveInputsAsync(
        AnalysisItem item,
        string executionId,
        Dictionary<string, IFeatureSource> externalSources)
    {
        var resolved = new Dictionary<string, IFeatureSource>();

        foreach (var (key, binding) in item.Inputs)
        {
            switch (binding.Type)
            {
                case BindingType.External:
                    if (externalSources.TryGetValue(key, out var existing))
                        await existing.DisposeAsync();
                    var source = _sourceFactory.CreateSource(binding);
                    externalSources[key] = source;
                    resolved[key] = source;
                    break;

                case BindingType.Upstream:
                    var cacheKey = $"{executionId}:{binding.SourceId}:{binding.OutputKey ?? "output"}";
                    var cached = await _resultCache.GetOrComputeAsync<IFeatureSource>(
                        cacheKey,
                        () => throw new InvalidOperationException(
                            $"上游分析项 '{binding.SourceId}' 的结果不可用（未执行或执行失败）"));
                    if (cached is null)
                        throw new InvalidOperationException(
                            $"上游分析项 '{binding.SourceId}' 的结果为空");
                    resolved[key] = cached;
                    break;

                case BindingType.SubPlan:
                    throw new NotSupportedException(
                        $"P1 不支持子方案绑定: '{key}' (SubPlan '{binding.SourceId}')");

                default:
                    throw new NotSupportedException(
                        $"不支持的绑定类型: {binding.Type}");
            }
        }

        return resolved;
    }

    private async Task HandleOutputsAsync(
        AnalysisItem item,
        ExecutionResult result,
        string executionId,
        CancellationToken ct)
    {
        foreach (var (key, value) in result.Outputs)
        {
            if (value is IFeatureSource featureSource)
            {
                var sink = _sinkFactory.CreateSink(item.Output);
                try
                {
                    var sinkSchema = new OutputSchema { Description = $"{item.Id}:{key}" };
                    await sink.InitializeAsync(sinkSchema, ct);

                    await foreach (var feature in featureSource.GetFeaturesAsync(cancellationToken: ct))
                    {
                        await sink.WriteAsync(feature, ct);
                    }

                    await sink.CompleteAsync(ct);
                }
                finally
                {
                    await SinkFactory.DisposeSinkAsync(sink);
                }
            }
        }
    }

    private async Task CacheOutputsForDownstream(string itemId, ExecutionResult result, string executionId)
    {
        foreach (var (key, value) in result.Outputs)
        {
            if (value is IFeatureSource source)
            {
                var cacheKey = $"{executionId}:{itemId}:{key}";
                await _resultCache.GetOrComputeAsync<IFeatureSource>(
                    cacheKey,
                    () => Task.FromResult(source));
            }
        }
    }

    private static bool HasFailedUpstream(AnalysisItem item, HashSet<string> failedItemIds)
    {
        return item.Inputs.Values.Any(b => b is { Type: BindingType.Upstream } && failedItemIds.Contains(b.SourceId));
    }

    private static void CollectIssues(ExecutionResult result, List<IssueRecord> allIssues)
    {
        foreach (var (_, value) in result.Outputs)
        {
            if (value is IEnumerable<IssueRecord> issues)
                allIssues.AddRange(issues);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error,
            Message = "方案 DAG 无效: {Error}")]
        public static partial void DagInvalid(ILogger logger, string error);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "拓扑排序不完整，可能存在循环依赖")]
        public static partial void SortIncomplete(ILogger logger);

        [LoggerMessage(Level = LogLevel.Information,
            Message = "开始执行: {ItemId} ({OperatorId})")]
        public static partial void ItemExecutionStart(ILogger logger, string itemId, string operatorId);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "分析项 {ItemId} 执行失败: {ErrorCode} - {Message}")]
        public static partial void ItemExecutionFailed(ILogger logger, string itemId, string errorCode, string message);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "失败策略为 StopOnAny，终止执行")]
        public static partial void StopOnAny(ILogger logger);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "分析项 {ItemId} 因上游依赖执行失败而跳过")]
        public static partial void ItemSkippedUpstreamFailed(ILogger logger, string itemId);

        [LoggerMessage(Level = LogLevel.Critical,
            Message = "方案执行因未受控异常而终止")]
        public static partial void PlanExecutionAborted(ILogger logger, Exception ex);
    }
}
