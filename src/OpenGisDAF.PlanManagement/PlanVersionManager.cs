namespace OpenGisDAF.PlanManagement;

using System.Text;
using Microsoft.Extensions.Logging;
using OpenGisDAF.Core;

public sealed class PlanVersionManager : IPlanVersionManager
{
    private readonly IPlanRepository _repository;
    private readonly IPlanSerializer _serializer;
    private readonly ILogger<PlanVersionManager>? _logger;

    public PlanVersionManager(
        IPlanRepository repository,
        IPlanSerializer serializer,
        ILogger<PlanVersionManager>? logger = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _logger = logger;
    }

    public async Task BackupAsync(string planId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(planId);

        var repo = GetPlanRepository();
        var filePath = repo.FindPlanFile(planId);
        if (filePath is null)
        {
            _logger?.LogWarning("Plan file not found for backup, plan ID: {PlanId}", planId);
            return;
        }

        var dir = Path.GetDirectoryName(filePath)!;
        var bakFiles = Directory.EnumerateFiles(dir, $"{planId}.V*.bak").ToList();

        int nextVersion = 1;
        if (bakFiles.Count > 0)
        {
            var maxVersion = 0;
            foreach (var bak in bakFiles)
            {
                var bakName = Path.GetFileNameWithoutExtension(bak);
                var vPart = bakName.Substring(planId.Length + 2);
                if (int.TryParse(vPart, out var v))
                    maxVersion = Math.Max(maxVersion, v);
            }

            nextVersion = maxVersion + 1;
        }

        var bakPath = Path.Combine(dir, $"{planId}.V{nextVersion}.bak");
        File.Copy(filePath, bakPath, overwrite: true);
        _logger?.LogInformation(
            "Backup created for plan {PlanId}: {BackupPath} (version {Version})",
            planId, bakPath, nextVersion);
    }

    public Task<IReadOnlyList<VersionHistoryEntry>> GetVersionHistoryAsync(
        string planId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(planId);

        var repo = GetPlanRepository();
        var filePath = repo.FindPlanFile(planId);
        if (filePath is null)
        {
            _logger?.LogWarning("Plan file not found for version history, plan ID: {PlanId}", planId);
            return Task.FromResult<IReadOnlyList<VersionHistoryEntry>>([]);
        }

        var dir = Path.GetDirectoryName(filePath)!;
        var bakFiles = Directory.EnumerateFiles(dir, $"{planId}.V*.bak");

        var entries = new List<VersionHistoryEntry>();
        foreach (var bak in bakFiles)
        {
            var bakName = Path.GetFileNameWithoutExtension(bak);
            var vPart = bakName.Substring(planId.Length + 2);
            if (!int.TryParse(vPart, out var v))
                continue;

            var fileInfo = new FileInfo(bak);
            entries.Add(new VersionHistoryEntry
            {
                PlanId = planId,
                VersionNumber = v,
                FilePath = bak,
                CreatedAt = fileInfo.CreationTime,
                FileSize = fileInfo.Length,
            });
        }

        entries.Sort((a, b) => b.VersionNumber.CompareTo(a.VersionNumber));
        return Task.FromResult<IReadOnlyList<VersionHistoryEntry>>(entries);
    }

    public async Task<AnalysisPlan> RollbackAsync(
        string planId,
        int versionNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(planId);

        var repo = GetPlanRepository();
        var history = await GetVersionHistoryAsync(planId, cancellationToken);
        var entry = history.FirstOrDefault(e => e.VersionNumber == versionNumber);

        if (entry is null)
        {
            throw new InvalidOperationException(
                $"Version {versionNumber} not found for plan '{planId}'.");
        }

        var json = await File.ReadAllTextAsync(entry.FilePath, cancellationToken);
        var plan = _serializer.Deserialize(json);

        await BackupAsync(planId, cancellationToken);

        var currentFilePath = repo.FindPlanFile(planId);
        if (currentFilePath is not null)
        {
            var newJson = _serializer.Serialize(plan);
            await File.WriteAllTextAsync(currentFilePath, newJson, cancellationToken);
            _logger?.LogInformation(
                "Rollback completed for plan {PlanId} to version {Version}",
                planId, versionNumber);
        }

        return plan;
    }

    public async Task<string> DiffAsync(
        string planId,
        int versionA,
        int versionB,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(planId);

        var linesA = await ReadVersionLinesAsync(planId, versionA, cancellationToken);
        var linesB = await ReadVersionLinesAsync(planId, versionB, cancellationToken);

        var maxLines = Math.Max(linesA.Count, linesB.Count);
        var sb = new StringBuilder();

        for (int i = 0; i < maxLines; i++)
        {
            var hasA = i < linesA.Count;
            var hasB = i < linesB.Count;

            if (hasA && hasB)
            {
                if (linesA[i] == linesB[i])
                {
                    sb.Append(' ').Append(' ').AppendLine(linesA[i]);
                }
                else
                {
                    sb.Append('~').Append(' ').Append('-').Append(' ').Append(linesA[i]).Append(' ').Append('|').Append(' ').Append('+').Append(' ').AppendLine(linesB[i]);
                }
            }
            else if (hasA)
            {
                sb.Append('-').Append(' ').AppendLine(linesA[i]);
            }
            else
            {
                sb.Append('+').Append(' ').AppendLine(linesB[i]);
            }
        }

        return sb.ToString();
    }

    private async Task<IReadOnlyList<string>> ReadVersionLinesAsync(
        string planId,
        int version,
        CancellationToken cancellationToken)
    {
        string content;

        if (version == 0)
        {
            var repo = GetPlanRepository();
            var filePath = repo.FindPlanFile(planId);
            if (filePath is null)
                throw new FileNotFoundException($"Plan file not found for plan ID: {planId}");

            content = await File.ReadAllTextAsync(filePath, cancellationToken);
        }
        else
        {
            var repo = GetPlanRepository();
            var filePath = repo.FindPlanFile(planId);
            if (filePath is null)
                throw new FileNotFoundException($"Plan file not found for plan ID: {planId}");

            var dir = Path.GetDirectoryName(filePath)!;
            var bakPath = Path.Combine(dir, $"{planId}.V{version}.bak");
            if (!File.Exists(bakPath))
                throw new FileNotFoundException($"Backup version {version} not found for plan '{planId}'.");

            content = await File.ReadAllTextAsync(bakPath, cancellationToken);
        }

        return content.Replace("\r\n", "\n").Split('\n');
    }

    private PlanRepository GetPlanRepository()
    {
        if (_repository is PlanRepository repo)
            return repo;

        throw new InvalidOperationException(
            "PlanVersionManager requires an IPlanRepository backed by PlanRepository.");
    }
}
