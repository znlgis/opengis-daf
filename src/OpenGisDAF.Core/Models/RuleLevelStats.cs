namespace OpenGisDAF.Core;

public sealed record RuleLevelStats
{
    public string RuleId { get; init; }
    public long TotalChecked { get; init; }
    public long Passed { get; init; }
    public long Failed { get; init; }
    public double PassRate { get; init; }
}
