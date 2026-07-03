namespace OpenGisDAF.Core;

public sealed record PlanSummary
{
    public string Id { get; init; } = null!;
    public string Name { get; init; } = null!;
    public string Version { get; init; } = null!;
    public string? Group { get; init; }
    public int ItemCount { get; init; }
    public int SubPlanCount { get; init; }
    public DateTime LastModified { get; init; }
}
