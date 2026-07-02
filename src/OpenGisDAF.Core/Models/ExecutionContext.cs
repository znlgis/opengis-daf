using System.Collections.Concurrent;

namespace OpenGisDAF.Core;

public class ExecutionContext
{
    public string ExecutionId { get; init; }
    public string PlanId { get; init; }
    public DateTimeOffset StartTime { get; init; }
    public CancellationToken CancellationToken { get; init; }
    public IProgress<ExecutionProgress>? Progress { get; init; }
    public ConcurrentDictionary<string, object?> SharedState { get; } = new();
    public ConcurrentDictionary<string, IFeatureSource> IntermediateResults { get; } = new();
}
