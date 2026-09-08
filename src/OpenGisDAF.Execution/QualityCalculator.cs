using OpenGisDAF.Core;

namespace OpenGisDAF.Execution;

public sealed class QualityCalculator
{
    public static double CalculateScore(
        IReadOnlyDictionary<string, List<IssueRecord>> issuesByItem,
        QualityReportConfig? config = null,
        IReadOnlyDictionary<string, long>? checkedCounts = null)
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

            var totalChecked = checkedCounts?.GetValueOrDefault(itemId, issues.Count) ?? issues.Count;
            var failedFeatures = issues
                .Where(i => i.Severity == IssueSeverity.Error)
                .Select(i => i.FeatureId)
                .Distinct(StringComparer.Ordinal)
                .Count();

            var passRate = totalChecked > 0
                ? Math.Max(0, 1.0 - (double)failedFeatures / totalChecked)
                : 1.0;

            totalWeightedScore += weight * passRate;
        }

        return totalWeight > 0
            ? Math.Round(totalWeightedScore / totalWeight * 100.0, 2)
            : 100.0;
    }

    public static IReadOnlyList<RuleLevelStats> CalculateRuleStats(
        IReadOnlyDictionary<string, List<IssueRecord>> issuesByItem,
        IReadOnlyDictionary<string, long>? checkedCounts = null)
    {
        var stats = new List<RuleLevelStats>();

        foreach (var (itemId, issues) in issuesByItem)
        {
            stats.Add(new RuleLevelStats
            {
                RuleId = itemId,
                TotalChecked = checkedCounts?.GetValueOrDefault(itemId, issues.Count) ?? issues.Count,
                Passed = Math.Max(0, (checkedCounts?.GetValueOrDefault(itemId, issues.Count) ?? issues.Count)
                    - issues.Where(i => i.Severity == IssueSeverity.Error)
                        .Select(i => i.FeatureId)
                        .Distinct(StringComparer.Ordinal)
                        .Count()),
                Failed = issues.Where(i => i.Severity == IssueSeverity.Error)
                    .Select(i => i.FeatureId)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                PassRate = (checkedCounts?.GetValueOrDefault(itemId, issues.Count) ?? issues.Count) > 0
                    ? Math.Round((double)Math.Max(0, (checkedCounts?.GetValueOrDefault(itemId, issues.Count) ?? issues.Count)
                        - issues.Where(i => i.Severity == IssueSeverity.Error)
                            .Select(i => i.FeatureId)
                            .Distinct(StringComparer.Ordinal)
                            .Count())
                        / (checkedCounts?.GetValueOrDefault(itemId, issues.Count) ?? issues.Count), 4)
                    : 1.0
            });
        }

        return stats;
    }
}
