namespace OpenGisDAF.Core;

public sealed record ExecutionMetadata
{
    public string PlanId { get; init; } = null!;
    public string PlanVersion { get; init; } = null!;
    public string OperatorVersion { get; init; } = null!;
    public IReadOnlyDictionary<string, string> DataSourceVersions { get; init; } = null!;
    public DateTimeOffset ExecutionTime { get; init; }
}
