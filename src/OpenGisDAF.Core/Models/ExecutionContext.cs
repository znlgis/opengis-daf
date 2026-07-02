using Microsoft.Extensions.Logging;

namespace OpenGisDAF.Core;

public sealed class ExecutionContext
{
    public string PlanId { get; init; }
    public string ExecutionId { get; init; }
    public IResultCache ResultCache { get; init; }
    public ILogger Logger { get; init; }
    public IServiceProvider Services { get; init; }
    public PlanExecutionStatistics Statistics { get; init; }
}
