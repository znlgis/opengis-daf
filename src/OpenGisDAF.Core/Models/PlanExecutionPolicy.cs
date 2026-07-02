namespace OpenGisDAF.Core;

public sealed class PlanExecutionPolicy
{
    public int MaxParallelism { get; init; } = 4;
    public GlobalConcurrencyPolicy? GlobalConcurrency { get; init; }
    public FailurePolicy FailurePolicy { get; init; } = FailurePolicy.StopOnAny;
    public bool EnablePartitioning { get; init; } = false;
    public int PartitionCount { get; init; } = 8;
    public QualityReportConfig? QualityReportConfig { get; init; }
}
