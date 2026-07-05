using Microsoft.Extensions.Logging;
using OpenGisDAF.Core;

namespace OpenGisDAF.PlanManagement;

public sealed class PlanManager : IPlanManager
{
    private readonly IPlanRepository _repository;
    private readonly IPlanSerializer _serializer;
    private readonly IPlanValidator _validator;
    private readonly IPlanVersionManager _versionManager;
    private readonly IOperatorPool? _operatorPool;
    private readonly ILogger<PlanManager>? _logger;

    public PlanManager(
        IPlanRepository repository,
        IPlanSerializer serializer,
        IPlanValidator validator,
        IPlanVersionManager versionManager,
        IOperatorPool? operatorPool = null,
        ILogger<PlanManager>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(versionManager);

        _repository = repository;
        _serializer = serializer;
        _validator = validator;
        _versionManager = versionManager;
        _operatorPool = operatorPool;
        _logger = logger;
    }

    public async Task<AnalysisPlan> CreateAsync(AnalysisPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var id = string.IsNullOrEmpty(plan.Id)
            ? Guid.NewGuid().ToString("N")
            : plan.Id;

        var version = string.IsNullOrEmpty(plan.Version)
            ? "1.0.0"
            : plan.Version;

        var normalized = new AnalysisPlan
        {
            Id = id,
            Name = plan.Name,
            Version = version,
            Group = plan.Group,
            Items = plan.Items,
            SubPlans = plan.SubPlans,
            ExecutionPolicy = plan.ExecutionPolicy,
        };

        _logger?.LogDebug("Creating plan {PlanId} v{Version}", normalized.Id, normalized.Version);

        await _repository.SaveAsync(normalized, cancellationToken);

        _logger?.LogInformation("Plan {PlanId} created successfully", normalized.Id);

        return normalized;
    }

    public async Task<AnalysisPlan?> LoadAsync(string planId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(planId);

        _logger?.LogDebug("Loading plan {PlanId}", planId);

        return await _repository.LoadAsync(planId, cancellationToken);
    }

    public async Task<AnalysisPlan> SaveAsync(AnalysisPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var bumpedVersion = BumpPatchVersion(plan.Version);

        var updated = new AnalysisPlan
        {
            Id = plan.Id,
            Name = plan.Name,
            Version = bumpedVersion,
            Group = plan.Group,
            Items = plan.Items,
            SubPlans = plan.SubPlans,
            ExecutionPolicy = plan.ExecutionPolicy,
        };

        _logger?.LogDebug("Saving plan {PlanId} v{OldVersion} -> v{NewVersion}",
            plan.Id, plan.Version, bumpedVersion);

        await _versionManager.BackupAsync(plan.Id, cancellationToken);
        await _repository.SaveAsync(updated, cancellationToken);

        _logger?.LogInformation("Plan {PlanId} saved as v{NewVersion}", plan.Id, bumpedVersion);

        return updated;
    }

    public async Task<AnalysisPlan> UpdateAsync(AnalysisPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        _logger?.LogDebug("Updating plan {PlanId} v{Version}", plan.Id, plan.Version);

        await _repository.SaveAsync(plan, cancellationToken);

        _logger?.LogInformation("Plan {PlanId} updated", plan.Id);

        return plan;
    }

    public async Task<AnalysisPlan> CopyAsync(string sourcePlanId, string newPlanId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePlanId);
        ArgumentNullException.ThrowIfNull(newPlanId);

        _logger?.LogDebug("Copying plan {SourcePlanId} to {NewPlanId}", sourcePlanId, newPlanId);

        var source = await LoadAsync(sourcePlanId, cancellationToken);
        if (source is null)
        {
            throw new KeyNotFoundException($"Source plan '{sourcePlanId}' not found.");
        }

        var copyName = string.IsNullOrEmpty(source.Name)
            ? "Copy"
            : source.Name + " (Copy)";

        var copy = new AnalysisPlan
        {
            Id = newPlanId,
            Name = copyName,
            Version = "1.0.0",
            Group = source.Group,
            Items = source.Items,
            SubPlans = source.SubPlans,
            ExecutionPolicy = source.ExecutionPolicy,
        };

        return await CreateAsync(copy, cancellationToken);
    }

    public async Task<AnalysisPlan> ImportAsync(string json, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(json);

        _logger?.LogDebug("Importing plan from JSON");

        var plan = _serializer.Deserialize(json);

        _logger?.LogDebug("Deserialized plan {PlanId}, validating", plan.Id);

        var validation = await _validator.ValidateAsync(plan, _operatorPool, cancellationToken);
        if (!validation.IsValid)
            throw new InvalidOperationException($"方案验证失败: {string.Join("; ", validation.Errors.Select(e => e.Message))}");

        _logger?.LogDebug("Plan {PlanId} validated, creating", plan.Id);

        return await CreateAsync(plan, cancellationToken);
    }

    public async Task<string> ExportAsync(string planId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(planId);

        _logger?.LogDebug("Exporting plan {PlanId}", planId);

        var plan = await LoadAsync(planId, cancellationToken);
        if (plan is null)
        {
            throw new KeyNotFoundException($"Plan '{planId}' not found.");
        }

        return _serializer.Serialize(plan);
    }

    public async Task<ValidationResult> ValidateAsync(string planId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(planId);

        _logger?.LogDebug("Validating plan {PlanId}", planId);

        var plan = await LoadAsync(planId, cancellationToken);
        if (plan is null)
        {
            _logger?.LogWarning("Validation failed: plan {PlanId} not found", planId);

            return new ValidationResult
            {
                Errors = new[]
                {
                    new ValidationError
                    {
                        Severity = ValidationSeverity.Error,
                        Code = ErrorCode.PlanNotFound,
                        Message = "Plan not found",
                    },
                },
            };
        }

        return await _validator.ValidateAsync(plan, _operatorPool, cancellationToken);
    }

    private static string BumpPatchVersion(string version)
    {
        var parts = version.Split('.');
        if (parts.Length >= 3
            && int.TryParse(parts[^1], out var patch))
        {
            parts[^1] = (patch + 1).ToString();
            return string.Join('.', parts);
        }

        return "1.0.1";
    }
}
