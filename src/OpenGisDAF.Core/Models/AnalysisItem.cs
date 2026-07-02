namespace OpenGisDAF.Core;

public sealed class AnalysisItem
{
    public string Id { get; init; } = null!;
    public string OperatorId { get; init; } = null!;
    public string? OperatorVersion { get; init; }
    public IReadOnlyDictionary<string, InputBinding> Inputs { get; init; } = new Dictionary<string, InputBinding>();
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();
    public OutputBinding Output { get; init; } = null!;
    public ItemExecutionPolicy ExecutionPolicy { get; init; } = new();
}
