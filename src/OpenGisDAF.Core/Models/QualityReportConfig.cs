namespace OpenGisDAF.Core;

public sealed class QualityReportConfig
{
    public IReadOnlyDictionary<string, double> RuleWeights { get; init; } = null!;
    public double MinPassRate { get; init; } = 0.95;
}
