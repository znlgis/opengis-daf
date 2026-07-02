namespace OpenGisDAF.Core;

public sealed record QcStatistics
{
    public int TotalIssues { get; init; }
    public IReadOnlyDictionary<string, int> IssuesBySeverity { get; init; } = null!;
    public IReadOnlyDictionary<string, int> IssuesByCategory { get; init; } = null!;
}
