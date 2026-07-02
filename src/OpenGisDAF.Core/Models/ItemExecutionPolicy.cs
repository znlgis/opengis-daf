namespace OpenGisDAF.Core;

public sealed class ItemExecutionPolicy
{
    public int MaxRetries { get; init; } = 0;
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(5);
    public bool ExponentialBackoff { get; init; } = true;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(30);
    public LogGranularity LogGranularity { get; init; } = LogGranularity.Item;
    public bool RetainIntermediateResults { get; init; } = false;
    public bool QcMode { get; init; } = false;
}
