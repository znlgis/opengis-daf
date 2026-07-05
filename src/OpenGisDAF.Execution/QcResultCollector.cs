using Microsoft.Extensions.Logging;
using OpenGisDAF.Core;

namespace OpenGisDAF.Execution;

public sealed partial class QcResultCollector
{
    private readonly ILogger<QcResultCollector> _logger;

    public QcResultCollector(ILogger<QcResultCollector> logger)
    {
        _logger = logger;
    }

    public QcCollectionResult Collect(IReadOnlyList<ExecutionResult> itemResults, IReadOnlyList<AnalysisItem> items)
    {
        var allIssues = new List<IssueRecord>();
        var itemIssues = new Dictionary<string, List<IssueRecord>>();

        foreach (var (result, item) in itemResults.Zip(items))
        {
            var issues = new List<IssueRecord>();
            foreach (var (_, value) in result.Outputs)
            {
                if (value is IEnumerable<IssueRecord> issueList)
                    issues.AddRange(issueList);
            }

            if (issues.Count > 0)
            {
                itemIssues[item.Id] = issues;
                allIssues.AddRange(issues);
            }
        }

        Log.QcCollectionCompleted(_logger, allIssues.Count);

        return new QcCollectionResult
        {
            AllIssues = allIssues,
            IssuesByItem = itemIssues,
            QcStats = BuildQcStatistics(allIssues)
        };
    }

    private static QcStatistics BuildQcStatistics(List<IssueRecord> issues)
    {
        return new QcStatistics
        {
            TotalIssues = issues.Count,
            IssuesBySeverity = issues
                .GroupBy(i => i.Severity.ToString())
                .ToDictionary(g => g.Key, g => g.Count()),
            IssuesByCategory = issues
                .GroupBy(i => i.IssueType)
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information,
            Message = "QC 收集完成: 共 {Count} 条问题记录")]
        public static partial void QcCollectionCompleted(ILogger logger, int count);
    }
}

public sealed record QcCollectionResult
{
    public IReadOnlyList<IssueRecord> AllIssues { get; init; } = [];
    public IReadOnlyDictionary<string, List<IssueRecord>> IssuesByItem { get; init; }
        = new Dictionary<string, List<IssueRecord>>();
    public QcStatistics QcStats { get; init; } = null!;
}
