namespace OpenGisDAF.Core;

public sealed record FieldDefinition
{
    public string Name { get; init; }
    public FieldType Type { get; init; }
    public bool Required { get; init; }
}
