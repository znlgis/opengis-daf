namespace OpenGisDAF.Core;

public sealed record FeatureSchemaMetadata
{
    public IReadOnlyList<FieldDefinition> Fields { get; init; } = [];
    public GeometryType? GeometryType { get; init; }
    public ISpatialReference? SpatialReference { get; init; }
}
