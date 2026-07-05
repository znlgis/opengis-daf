using System.Text.Json;
using OpenGisDAF.Core;
using OpenGisDAF.Infrastructure;

namespace OpenGisDAF.Execution;

public sealed class QualityReportGenerator
{
    public static QualityReport Generate(
        IReadOnlyList<IssueRecord> allIssues,
        IReadOnlyDictionary<string, List<IssueRecord>> issuesByItem,
        AnalysisPlan plan,
        string executionId,
        QualityReportConfig? config = null)
    {
        var score = QualityCalculator.CalculateScore(issuesByItem, config);
        var ruleStats = QualityCalculator.CalculateRuleStats(issuesByItem);

        var ruleStatsDict = ruleStats.ToDictionary(r => r.RuleId);

        return new QualityReport
        {
            TotalScore = score,
            RuleStats = ruleStatsDict,
            Issues = allIssues,
            Metadata = new ExecutionMetadata
            {
                PlanId = plan.Id,
                PlanVersion = plan.Version,
                OperatorVersion = "1.0.0",
                ExecutionTime = DateTimeOffset.UtcNow
            }
        };
    }

    public static async Task SaveAsync(QualityReport report, string filePath, CancellationToken ct = default)
    {
        var options = JsonConfiguration.Create();
        var json = JsonSerializer.Serialize(report, options);
        var dir = Path.GetDirectoryName(filePath);
        if (dir is not null)
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(filePath, json, ct);
    }
}
