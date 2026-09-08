namespace OpenGisDAF.Core;

public sealed record QcStatistics
{
    public int TotalIssues { get; init; }
    public IReadOnlyDictionary<string, long> CheckedCountsByItem { get; init; } = new Dictionary<string, long>();
    public IReadOnlyDictionary<string, int> IssuesBySeverity { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> IssuesByCategory { get; init; } = new Dictionary<string, int>();
}
