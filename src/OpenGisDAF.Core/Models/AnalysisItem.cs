namespace OpenGisDAF.Core;

public sealed class AnalysisItem
{
    public string Id { get; init; } = null!;
    public string OperatorId { get; init; } = null!;
    public string? OperatorVersion { get; init; }
    public IReadOnlyDictionary<string, InputBinding> Inputs { get; init; } = null!;
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = null!;
    public OutputBinding Output { get; init; } = null!;
    public ItemExecutionPolicy ExecutionPolicy { get; init; } = null!;
}
