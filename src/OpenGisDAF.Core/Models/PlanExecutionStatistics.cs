namespace OpenGisDAF.Core;

public sealed record PlanExecutionStatistics
{
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }
    public TimeSpan TotalElapsed { get; init; }
    public IReadOnlyList<PerItemStats> ItemStats { get; init; } = null!;
    public QcStatistics? QcStats { get; init; }
    public ResourceUsage? ResourceUsage { get; init; }
}
