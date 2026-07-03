namespace OpenGisDAF.PlanManagement;

using Microsoft.Extensions.Logging;
using OpenGisDAF.Core;

public sealed class PlanRepository : IPlanRepository
{
    private readonly string _rootPath;
    private readonly IPlanSerializer _serializer;
    private readonly ILogger<PlanRepository>? _logger;

    public PlanRepository(
        string rootPath,
        IPlanSerializer serializer,
        ILogger<PlanRepository>? logger = null)
    {
        _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _logger = logger;

        if (!Directory.Exists(_rootPath))
            Directory.CreateDirectory(_rootPath);
    }

    private static void ValidateSafePathSegment(string? segment, string paramName)
    {
        if (segment is null)
            return;

        if (segment.Contains("..") || segment.Contains('\\') || segment.Contains('/'))
            throw new ArgumentException(
                $"Path segment '{segment}' contains invalid characters.", paramName);
    }

    public string? FindPlanFile(string planId)
    {
        ValidateSafePathSegment(planId, nameof(planId));
        return FindPlanFileInternal(planId);
    }

    private string? FindPlanFileInternal(string planId)
    {
        if (!Directory.Exists(_rootPath))
            return null;

        foreach (var subDir in Directory.EnumerateDirectories(_rootPath))
        {
            var filePath = Path.Combine(subDir, $"{planId}.json");
            if (File.Exists(filePath))
                return filePath;
        }

        return null;
    }

    public async Task SaveAsync(AnalysisPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        ValidateSafePathSegment(plan.Id, nameof(plan.Id));
        ValidateSafePathSegment(plan.Group, nameof(plan.Group));

        var group = plan.Group ?? "default";
        var dir = Path.Combine(_rootPath, group);
        Directory.CreateDirectory(dir);

        var filePath = Path.Combine(dir, $"{plan.Id}.json");
        var json = _serializer.Serialize(plan);

        await File.WriteAllTextAsync(filePath, json, cancellationToken);
        _logger?.LogInformation("Plan {PlanId} saved to {FilePath}", plan.Id, filePath);
    }

    public async Task<AnalysisPlan?> LoadAsync(string planId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(planId);

        ValidateSafePathSegment(planId, nameof(planId));
        var filePath = FindPlanFileInternal(planId);
        if (filePath is null)
        {
            _logger?.LogWarning("Plan file not found for plan ID: {PlanId}", planId);
            return null;
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var plan = _serializer.Deserialize(json);
        _logger?.LogInformation("Plan {PlanId} loaded from {FilePath}", planId, filePath);
        return plan;
    }

    public async Task<bool> DeleteAsync(string planId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(planId);

        ValidateSafePathSegment(planId, nameof(planId));
        var filePath = FindPlanFileInternal(planId);
        if (filePath is null)
        {
            _logger?.LogWarning("Plan file not found for deletion, plan ID: {PlanId}", planId);
            return false;
        }

        File.Delete(filePath);
        _logger?.LogInformation("Plan {PlanId} deleted from {FilePath}", planId, filePath);
        return true;
    }

    public Task<bool> ExistsAsync(string planId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(planId);

        ValidateSafePathSegment(planId, nameof(planId));
        var filePath = FindPlanFileInternal(planId);
        return Task.FromResult(filePath is not null);
    }

    public async Task<IReadOnlyList<PlanSummary>> ListAsync(
        string? group = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSafePathSegment(group, nameof(group));

        var summaries = new List<PlanSummary>();

        if (group is not null)
        {
            var dir = Path.Combine(_rootPath, group);
            if (Directory.Exists(dir))
                await ScanDirectoryAsync(dir, group, summaries, cancellationToken);
        }
        else if (Directory.Exists(_rootPath))
        {
            foreach (var subDir in Directory.EnumerateDirectories(_rootPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var subDirName = Path.GetFileName(subDir);
                await ScanDirectoryAsync(subDir, subDirName, summaries, cancellationToken);
            }
        }

        summaries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return summaries;
    }

    private async Task ScanDirectoryAsync(
        string dir,
        string group,
        List<PlanSummary> summaries,
        CancellationToken cancellationToken)
    {
        foreach (var filePath in Directory.EnumerateFiles(dir, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var json = await File.ReadAllTextAsync(filePath, cancellationToken);
                var plan = _serializer.Deserialize(json);
                var fileInfo = new FileInfo(filePath);

                summaries.Add(new PlanSummary
                {
                    Id = plan.Id,
                    Name = plan.Name,
                    Version = plan.Version,
                    Group = plan.Group ?? group,
                    ItemCount = plan.Items.Count,
                    SubPlanCount = plan.SubPlans.Count,
                    LastModified = fileInfo.LastWriteTime,
                });
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to deserialize plan file: {FilePath}", filePath);
            }
        }
    }
}
