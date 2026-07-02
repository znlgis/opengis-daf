namespace OpenGisDAF.Core;

public sealed record InputSchema
{
    public IReadOnlyList<FieldDefinition> RequiredFields { get; init; } = [];
    public GeometryType? RequiredGeometryType { get; init; }
    public string? Description { get; init; }
}
