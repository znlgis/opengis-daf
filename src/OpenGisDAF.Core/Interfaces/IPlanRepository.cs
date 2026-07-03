namespace OpenGisDAF.Core;

public interface IPlanRepository
{
    Task SaveAsync(AnalysisPlan plan, CancellationToken cancellationToken = default);
    Task<AnalysisPlan?> LoadAsync(string planId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string planId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlanSummary>> ListAsync(string? group = null, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string planId, CancellationToken cancellationToken = default);
    string? FindPlanFile(string planId);
}
