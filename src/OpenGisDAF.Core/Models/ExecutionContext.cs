using Microsoft.Extensions.Logging;

namespace OpenGisDAF.Core;

public sealed class ExecutionContext
{
    public ExecutionContext(string planId, string executionId, IResultCache resultCache,
        ILogger logger, IServiceProvider services, PlanExecutionStatistics statistics)
    {
        PlanId = planId;
        ExecutionId = executionId;
        ResultCache = resultCache;
        Logger = logger;
        Services = services;
        Statistics = statistics;
    }

    public string PlanId { get; init; } = null!;
    public string ExecutionId { get; init; } = null!;
    public IResultCache ResultCache { get; init; } = null!;
    public ILogger Logger { get; init; } = null!;
    public IServiceProvider Services { get; init; } = null!;
    public PlanExecutionStatistics Statistics { get; init; } = null!;

    public ExecutionContext() { } // for deserialization
}
