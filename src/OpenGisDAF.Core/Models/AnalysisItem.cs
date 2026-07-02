namespace OpenGisDAF.Core;

public sealed class AnalysisItem
{
    public string Id { get; init; }
    public string OperatorId { get; init; }
    public string? OperatorVersion { get; init; }
    public IReadOnlyDictionary<string, InputBinding> Inputs { get; init; }
    public IReadOnlyDictionary<string, object?> Parameters { get; init; }
    public OutputBinding Output { get; init; }
    public ItemExecutionPolicy ExecutionPolicy { get; init; }
}
