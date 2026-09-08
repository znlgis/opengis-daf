namespace OpenGisDAF.Core;

public sealed record OutputSchema
{
    public IReadOnlyList<FieldDefinition> ProducedFields { get; init; } = [];
    public GeometryType? ProducedGeometryType { get; init; }
    public string? Description { get; init; }
    public int? SpatialReferenceWkid { get; init; }
    public string? SpatialReferenceWkt { get; init; }
}
