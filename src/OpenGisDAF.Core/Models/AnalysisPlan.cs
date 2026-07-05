namespace OpenGisDAF.Core;

public sealed class AnalysisPlan
{
    public AnalysisPlan(string id, string name, string version)
    {
        Id = id;
        Name = name;
        Version = version;
    }

    public string Id { get; init; } = null!;
    public string Name { get; init; } = null!;
    public string? Group { get; init; }
    public string Version { get; init; } = null!;
    public IReadOnlyList<AnalysisItem> Items { get; init; } = [];
    public IReadOnlyList<AnalysisPlan> SubPlans { get; init; } = [];
    public PlanExecutionPolicy ExecutionPolicy { get; init; } = new();

    public AnalysisPlan() { } // for deserialization
}
