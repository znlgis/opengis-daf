namespace OpenGisDAF.Core;

public sealed record ParameterDefinition
{
    public string Name { get; init; }
    public string Type { get; init; }
    public bool Required { get; init; }
    public object? DefaultValue { get; init; }
    public string? Description { get; init; }
    public ParameterConstraint? Constraint { get; init; }
}
