namespace OpenGisDAF.Core;

public sealed record FeatureSourceMetadata
{
    public string SourceId { get; init; } = null!;
    public string SourceType { get; init; } = null!;
    public long? FeatureCount { get; init; }
    public string? Description { get; init; }
}
