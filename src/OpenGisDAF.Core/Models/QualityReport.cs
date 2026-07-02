namespace OpenGisDAF.Core;

public sealed record QualityReport
{
    public double TotalScore { get; init; }
    public IReadOnlyDictionary<string, RuleLevelStats> RuleStats { get; init; } = null!;
    public IReadOnlyList<IssueRecord> Issues { get; init; } = null!;
    public ExecutionMetadata Metadata { get; init; } = null!;
}
