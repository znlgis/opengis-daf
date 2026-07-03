namespace OpenGisDAF.Core;

public interface IPlanVersionManager
{
    Task<IReadOnlyList<VersionHistoryEntry>> GetVersionHistoryAsync(string planId, CancellationToken cancellationToken = default);
    Task<AnalysisPlan> RollbackAsync(string planId, int versionNumber, CancellationToken cancellationToken = default);
    Task BackupAsync(string planId, CancellationToken cancellationToken = default);
    Task<string> DiffAsync(string planId, int versionA, int versionB, CancellationToken cancellationToken = default);
}
