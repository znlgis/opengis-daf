using OpenGisDAF.Core;

namespace OpenGisDAF.Execution;

public sealed class QualityCalculator
{
    public static double CalculateScore(
        IReadOnlyDictionary<string, List<IssueRecord>> issuesByItem,
        QualityReportConfig? config = null)
    {
        if (issuesByItem.Count == 0)
            return 100.0;

        var weights = config?.RuleWeights ?? new Dictionary<string, double>();
        var totalWeightedScore = 0.0;
        var totalWeight = 0.0;

        foreach (var (itemId, issues) in issuesByItem)
        {
            var weight = weights.TryGetValue(itemId, out var w) ? w : 1.0;
            totalWeight += weight;

            var totalIssues = issues.Count;
            var errorIssues = issues.Count(i => i.Severity == IssueSeverity.Error);

            var passRate = totalIssues > 0
                ? Math.Max(0, 1.0 - (double)errorIssues / Math.Max(totalIssues, 1))
                : 1.0;

            totalWeightedScore += weight * passRate;
        }

        return totalWeight > 0
            ? Math.Round(totalWeightedScore / totalWeight * 100.0, 2)
            : 100.0;
    }

    public static IReadOnlyList<RuleLevelStats> CalculateRuleStats(
        IReadOnlyDictionary<string, List<IssueRecord>> issuesByItem)
    {
        var stats = new List<RuleLevelStats>();

        foreach (var (itemId, issues) in issuesByItem)
        {
            stats.Add(new RuleLevelStats
            {
                RuleId = itemId,
                TotalChecked = issues.Count,
                Passed = issues.Count(i => i.Severity != IssueSeverity.Error),
                Failed = issues.Count(i => i.Severity == IssueSeverity.Error),
                PassRate = issues.Count > 0
                    ? Math.Round((double)issues.Count(i => i.Severity != IssueSeverity.Error) / issues.Count, 4)
                    : 1.0
            });
        }

        return stats;
    }
}
