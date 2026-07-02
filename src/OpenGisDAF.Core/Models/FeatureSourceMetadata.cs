namespace OpenGisDAF.Core;

public sealed record FeatureSourceMetadata
{
    public string SourceId { get; init; }
    public string SourceType { get; init; }
    public long? FeatureCount { get; init; }
    public string? Description { get; init; }
}
