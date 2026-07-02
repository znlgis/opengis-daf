namespace OpenGisDAF.Core;

public sealed record ParameterConstraint
{
    public double? MinValue { get; init; }
    public double? MaxValue { get; init; }
    public string? Pattern { get; init; }
    public string[]? AllowedValues { get; init; }
}
