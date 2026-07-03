namespace OpenGisDAF.Core;

public sealed class AnalysisPlan
{
    public string Id { get; init; } = null!;
    public string Name { get; init; } = null!;
    public string? Group { get; init; }
    public string Version { get; init; } = null!;
    public IReadOnlyList<AnalysisItem> Items { get; init; } = [];
    public IReadOnlyList<AnalysisPlan> SubPlans { get; init; } = [];
    public PlanExecutionPolicy ExecutionPolicy { get; init; } = new();
}
