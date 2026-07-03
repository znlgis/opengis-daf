namespace OpenGisDAF.Core;

public sealed record VersionHistoryEntry
{
    public string PlanId { get; init; } = null!;
    public int VersionNumber { get; init; }
    public string FilePath { get; init; } = null!;
    public DateTime CreatedAt { get; init; }
    public long FileSize { get; init; }
}
