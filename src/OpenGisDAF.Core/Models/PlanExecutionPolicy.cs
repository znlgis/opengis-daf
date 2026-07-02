namespace OpenGisDAF.Core;

public sealed class PlanExecutionPolicy
{
    public int MaxParallelism { get; init; } = 4;
    public GlobalConcurrencyPolicy? GlobalConcurrency { get; init; }
}
