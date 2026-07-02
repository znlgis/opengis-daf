namespace OpenGisDAF.Core;

public sealed class IssueRecord
{
    public string IssueId { get; init; }
    public string PlanId { get; init; }
    public string ExecutionId { get; init; }
    public string ItemId { get; init; }
    public string FeatureId { get; init; }
    public string IssueType { get; init; }
    public IssueSeverity Severity { get; init; }
    public string Description { get; init; }
    public IReadOnlyDictionary<string, object?> ContextData { get; init; }
    public NetTopologySuite.Geometries.Geometry? ViolationGeometry { get; init; }
}
