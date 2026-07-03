namespace OpenGisDAF.Core;

public interface IPlanManager
{
    Task<AnalysisPlan> CreateAsync(AnalysisPlan plan, CancellationToken cancellationToken = default);
    Task<AnalysisPlan?> LoadAsync(string planId, CancellationToken cancellationToken = default);
    Task<AnalysisPlan> SaveAsync(AnalysisPlan plan, CancellationToken cancellationToken = default);
    Task<AnalysisPlan> UpdateAsync(AnalysisPlan plan, CancellationToken cancellationToken = default);
    Task<AnalysisPlan> CopyAsync(string sourcePlanId, string newPlanId, CancellationToken cancellationToken = default);
    Task<AnalysisPlan> ImportAsync(string json, CancellationToken cancellationToken = default);
    Task<string> ExportAsync(string planId, CancellationToken cancellationToken = default);
    Task<ValidationResult> ValidateAsync(string planId, CancellationToken cancellationToken = default);
}
