namespace OpenGisDAF.Core;

public sealed record QualityReport
{
    public double TotalScore { get; init; }
    public IReadOnlyDictionary<string, RuleLevelStats> RuleStats { get; init; } = new Dictionary<string, RuleLevelStats>();
    public IReadOnlyList<IssueRecord> Issues { get; init; } = [];
    public ExecutionMetadata Metadata { get; init; } = null!;
}
