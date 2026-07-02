namespace OpenGisDAF.Core;

public sealed record PerItemStats
{
    public string ItemId { get; init; }
    public string OperatorId { get; init; }
    public TimeSpan Elapsed { get; init; }
    public long FeaturesProcessed { get; init; }
    public long SuccessCount { get; init; }
    public long FailedCount { get; init; }
    public long SkippedCount { get; init; }
}
