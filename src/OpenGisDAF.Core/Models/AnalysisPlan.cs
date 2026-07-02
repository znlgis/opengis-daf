namespace OpenGisDAF.Core;

public sealed class AnalysisPlan
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string Version { get; init; }
    public IReadOnlyList<AnalysisItem> Items { get; init; }
    public IReadOnlyList<AnalysisPlan> SubPlans { get; init; }
    public PlanExecutionPolicy ExecutionPolicy { get; init; }
}
