namespace OpenGisDAF.Core;

public sealed record OperatorMetadata
{
    public string Id { get; init; } = null!;
    public string Name { get; init; } = null!;
    public string Category { get; init; } = null!;
    public string Description { get; init; } = null!;
    public string[] Tags { get; init; } = null!;
    public string Version { get; init; } = null!;
    public string? MinFrameworkVersion { get; init; }
    public string? CompatibilityNotes { get; init; }
    public bool SupportsIncremental { get; init; } = false;
    public IReadOnlyList<ParameterDefinition> Parameters { get; init; } = [];
    public InputSchema InputSchema { get; init; } = null!;
    public OutputSchema OutputSchema { get; init; } = null!;
}
