namespace OpenGisDAF.Core;

public sealed record ExecutionProgress
{
    public string ItemId { get; init; }
    public ExecutionStatus Status { get; init; }
    public long? FeaturesProcessed { get; init; }
    public long? TotalFeatures { get; init; }
}
