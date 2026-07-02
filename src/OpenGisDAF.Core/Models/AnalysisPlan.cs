namespace OpenGisDAF.Core;

public sealed class AnalysisPlan
{
    public string Id { get; init; } = null!;
    public string Name { get; init; } = null!;
    public string Version { get; init; } = null!;
    public IReadOnlyList<AnalysisItem> Items { get; init; } = null!;
    public IReadOnlyList<AnalysisPlan> SubPlans { get; init; } = null!;
    public PlanExecutionPolicy ExecutionPolicy { get; init; } = null!;
}
