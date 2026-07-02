namespace OpenGisDAF.Core;

public sealed class IssueRecord
{
    public string IssueId { get; init; } = Guid.NewGuid().ToString();
    public string PlanId { get; init; } = null!;
    public string ExecutionId { get; init; } = null!;
    public string ItemId { get; init; } = null!;
    public string FeatureId { get; init; } = null!;
    public string IssueType { get; init; } = null!;
    public IssueSeverity Severity { get; init; }
    public string Description { get; init; } = null!;
    public IReadOnlyDictionary<string, object?> ContextData { get; init; } = new Dictionary<string, object?>();
    public NetTopologySuite.Geometries.Geometry? ViolationGeometry { get; init; }
}
