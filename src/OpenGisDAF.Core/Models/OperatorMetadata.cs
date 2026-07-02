namespace OpenGisDAF.Core;

public sealed record OperatorMetadata
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string Category { get; init; }
    public string Description { get; init; }
    public string[] Tags { get; init; }
    public string Version { get; init; }
    public string? MinFrameworkVersion { get; init; }
    public string? CompatibilityNotes { get; init; }
    public bool SupportsIncremental { get; init; } = false;
    public IReadOnlyList<ParameterDefinition> Parameters { get; init; }
    public InputSchema InputSchema { get; init; }
    public OutputSchema OutputSchema { get; init; }
}
