namespace OpenGisDAF.Core;

public sealed record ExecutionMetadata
{
    public string PlanId { get; init; }
    public string PlanVersion { get; init; }
    public string OperatorVersion { get; init; }
    public IReadOnlyDictionary<string, string> DataSourceVersions { get; init; }
    public DateTimeOffset ExecutionTime { get; init; }
}
