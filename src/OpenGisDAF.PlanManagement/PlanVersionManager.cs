namespace OpenGisDAF.PlanManagement;

using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenGisDAF.Core;

public sealed class PlanVersionManager : IPlanVersionManager
{
    private static readonly ConcurrentDictionary<string, object> _backupLocks = new();
    private static readonly Regex _versionPattern = new(@"\.V(\d+)\.bak$", RegexOptions.Compiled);

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

        var filePath = await _repository.FindPlanFileAsync(planId, cancellationToken);
        if (filePath is null)
        {
            _logger?.LogWarning("Plan file not found for backup, plan ID: {PlanId}", planId);
            return;
        }

        var dir = Path.GetDirectoryName(filePath)!;

        var lockObj = _backupLocks.GetOrAdd(planId, _ => new object());
        lock (lockObj)
        {
            var bakFiles = Directory.EnumerateFiles(dir, $"{planId}.V*.bak").ToList();

            int nextVersion = 1;
            if (bakFiles.Count > 0)
            {
                var maxVersion = 0;
                foreach (var bak in bakFiles)
                {
                    var match = _versionPattern.Match(bak);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var v))
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

        _backupLocks.TryRemove(planId, out _);
    }

    public async Task<IReadOnlyList<VersionHistoryEntry>> GetVersionHistoryAsync(
        string planId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(planId);

        var filePath = await _repository.FindPlanFileAsync(planId, cancellationToken);
        if (filePath is null)
        {
            _logger?.LogWarning("Plan file not found for version history, plan ID: {PlanId}", planId);
            return [];
        }

        var dir = Path.GetDirectoryName(filePath)!;
        var bakFiles = Directory.EnumerateFiles(dir, $"{planId}.V*.bak");

        var entries = new List<VersionHistoryEntry>();
        foreach (var bak in bakFiles)
        {
            var match = _versionPattern.Match(bak);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var v))
                continue;

            var fileInfo = new FileInfo(bak);
            entries.Add(new VersionHistoryEntry
            {
                PlanId = planId,
                VersionNumber = v,
                FilePath = bak,
                CreatedAt = fileInfo.LastWriteTime,
                FileSize = fileInfo.Length,
            });
        }

        entries.Sort((a, b) => b.VersionNumber.CompareTo(a.VersionNumber));
        return entries;
    }

    public async Task<AnalysisPlan> RollbackAsync(
        string planId,
        int versionNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(planId);

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

        var currentFilePath = await _repository.FindPlanFileAsync(planId, cancellationToken);
        if (currentFilePath is not null)
        {
            var newJson = _serializer.Serialize(plan);
            var tmpPath = currentFilePath + ".tmp";
            try
            {
                await File.WriteAllTextAsync(tmpPath, newJson, cancellationToken);
                File.Move(tmpPath, currentFilePath, overwrite: true);
                _logger?.LogInformation(
                    "Rollback completed for plan {PlanId} to version {Version}",
                    planId, versionNumber);
            }
            catch
            {
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                throw;
            }
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
            var filePath = await _repository.FindPlanFileAsync(planId, cancellationToken);
            if (filePath is null)
                throw new FileNotFoundException($"Plan file not found for plan ID: {planId}");

            content = await File.ReadAllTextAsync(filePath, cancellationToken);
        }
        else
        {
            var filePath = await _repository.FindPlanFileAsync(planId, cancellationToken);
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
}
