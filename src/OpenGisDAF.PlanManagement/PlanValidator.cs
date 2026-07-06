namespace OpenGisDAF.PlanManagement;

using System.Text.Json;
using System.Text.RegularExpressions;
using OpenGisDAF.Core;

public sealed partial class PlanValidator : IPlanValidator
{
    [GeneratedRegex(@"^\d+\.\d+\.\d+$")]
    private static partial Regex SemverRegex();

    public Task<ValidationResult> ValidateAsync(
        AnalysisPlan plan,
        IOperatorPool? operatorPool = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var errors = new List<ValidationError>();
        var warnings = new List<ValidationError>();

        ValidateSchema(plan, errors);

        if (operatorPool is not null)
        {
            ValidateBusinessRules(plan, operatorPool, errors, warnings);
        }

        return Task.FromResult(new ValidationResult
        {
            Errors = errors.AsReadOnly(),
            Warnings = warnings.AsReadOnly()
        });
    }

    // ===== Phase A: Schema validation (rules 1-14, always run) =====

    private static void ValidateSchema(AnalysisPlan plan, List<ValidationError> errors)
    {
        // Rule 1: plan.Id not null/empty/whitespace
        if (string.IsNullOrWhiteSpace(plan.Id))
            errors.Add(NewError(ErrorCode.CfgSchemaInvalid, "Plan ID must not be null, empty, or whitespace.", null));

        // Rule 2: plan.Name not null/empty/whitespace
        if (string.IsNullOrWhiteSpace(plan.Name))
            errors.Add(NewError(ErrorCode.CfgSchemaInvalid, "Plan name must not be null, empty, or whitespace.", null));

        // Rule 3: plan.Version not null/empty, match semver ^\d+\.\d+\.\d+$
        if (string.IsNullOrWhiteSpace(plan.Version))
        {
            errors.Add(NewError(ErrorCode.CfgSchemaInvalid, "Plan version must not be null or empty.", null));
        }
        else if (!SemverRegex().IsMatch(plan.Version))
        {
            errors.Add(NewError(ErrorCode.CfgSchemaInvalid,
                $"Plan version '{plan.Version}' does not match semver format (e.g., 1.0.0).", null));
        }

        // Rule 4: plan.Items not null and count > 0
        if (plan.Items is null || plan.Items.Count == 0)
        {
            errors.Add(NewError(ErrorCode.CfgSchemaInvalid, "Plan must contain at least one analysis item.", null));
        }
        else
        {
            var itemIds = new HashSet<string>();

            for (int i = 0; i < plan.Items.Count; i++)
            {
                var item = plan.Items[i];
                var loc = $"plan/{i}";

                // Rule 5: item.Id not null/empty/whitespace
                if (string.IsNullOrWhiteSpace(item.Id))
                {
                    errors.Add(NewError(ErrorCode.CfgSchemaInvalid,
                        $"Item at index {i} has null, empty, or whitespace ID.", loc));
                }
                else
                {
                    // Rule 7: item.Id unique across plan
                    if (!itemIds.Add(item.Id))
                        errors.Add(NewError(ErrorCode.CfgSchemaInvalid,
                            $"Duplicate item ID '{item.Id}' found in plan.", loc));
                }

                // Rule 6: item.OperatorId not null/empty/whitespace
                if (string.IsNullOrWhiteSpace(item.OperatorId))
                    errors.Add(NewError(ErrorCode.CfgSchemaInvalid,
                        $"Item at index {i} has null, empty, or whitespace operator ID.", loc));

                // Rule 8: item.Inputs not null
                if (item.Inputs is null)
                {
                    errors.Add(NewError(ErrorCode.CfgSchemaInvalid,
                        $"Item at index {i} has null Inputs.", loc));
                }
                else
                {
                    // Rule 9: each input binding SourceId not null/empty
                    foreach (var (inputKey, binding) in item.Inputs)
                    {
                        if (string.IsNullOrWhiteSpace(binding.SourceId))
                            errors.Add(NewError(ErrorCode.CfgSchemaInvalid,
                                $"Input '{inputKey}' of item at index {i} has null or empty SourceId.", loc));

                        // Rule 10: if BindingType == SubPlan, SourceId must exist in plan.SubPlans
                        if (binding.Type == BindingType.SubPlan && !string.IsNullOrWhiteSpace(binding.SourceId))
                        {
                            if (plan.SubPlans is null || plan.SubPlans.Count == 0 ||
                                !plan.SubPlans.Any(sp => sp.Id == binding.SourceId))
                                errors.Add(NewError(ErrorCode.CfgSchemaInvalid,
                                    $"SubPlan reference '{binding.SourceId}' in item at index {i} input '{inputKey}' not found in plan.SubPlans.", loc));
                        }
                    }
                }

                // Rule 11: item.ExecutionPolicy.MaxRetries >= 0
                if (item.ExecutionPolicy.MaxRetries < 0)
                    errors.Add(NewError(ErrorCode.CfgSchemaInvalid,
                        $"Item at index {i} MaxRetries must be >= 0, got {item.ExecutionPolicy.MaxRetries}.", loc));

                // Rule 12: item.ExecutionPolicy.Timeout > TimeSpan.Zero
                if (item.ExecutionPolicy.Timeout <= TimeSpan.Zero)
                    errors.Add(NewError(ErrorCode.CfgSchemaInvalid,
                        $"Item at index {i} Timeout must be greater than zero.", loc));
            }
        }

        // Rule 13: plan.ExecutionPolicy.MaxParallelism >= 1
        if (plan.ExecutionPolicy.MaxParallelism < 1)
            errors.Add(NewError(ErrorCode.CfgSchemaInvalid,
                $"Plan MaxParallelism must be >= 1, got {plan.ExecutionPolicy.MaxParallelism}.", null));

        // Rule 14: PartitionCount >= 1 if EnablePartitioning == true
        if (plan.ExecutionPolicy.EnablePartitioning && plan.ExecutionPolicy.PartitionCount < 1)
            errors.Add(NewError(ErrorCode.CfgSchemaInvalid,
                $"Plan PartitionCount must be >= 1 when partitioning is enabled, got {plan.ExecutionPolicy.PartitionCount}.", null));

        // Rule 14a: GlobalConcurrency.MaxGlobalParallelism >= 1 when enabled
        if (plan.ExecutionPolicy.GlobalConcurrency is { Enabled: true } gc && gc.MaxGlobalParallelism < 1)
            errors.Add(NewError(ErrorCode.CfgSchemaInvalid,
                $"GlobalConcurrency.MaxGlobalParallelism must be >= 1 when enabled, got {gc.MaxGlobalParallelism}.", null));

        // Rule 14b: RetryInterval must be positive when retries are configured without exponential backoff
        if (plan.Items is not null)
        {
            foreach (var item in plan.Items)
            {
                if (item.ExecutionPolicy.MaxRetries > 0 && !item.ExecutionPolicy.ExponentialBackoff && item.ExecutionPolicy.RetryInterval <= TimeSpan.Zero)
                    errors.Add(NewError(ErrorCode.CfgSchemaInvalid,
                        $"Item '{item.Id}' has MaxRetries={item.ExecutionPolicy.MaxRetries} but RetryInterval must be > 0 when ExponentialBackoff is disabled.", $"item.{item.Id}"));
            }
        }

        // Rule 17: DAG cycle detection
        if (plan.Items is { Count: > 1 })
        {
            var validItems = plan.Items.Where(i => !string.IsNullOrWhiteSpace(i.Id)).ToList();
            var itemMap = validItems.ToDictionary(i => i.Id);
            ValidateDagNoCycle(validItems, itemMap, errors);
        }
    }

    // ===== Phase B: Business rule validation (rules 15-21, only when operatorPool != null) =====

    private static void ValidateBusinessRules(
        AnalysisPlan plan, IOperatorPool operatorPool,
        List<ValidationError> errors, List<ValidationError> warnings)
    {
        var itemMap = plan.Items.Where(i => !string.IsNullOrWhiteSpace(i.Id))
                                .ToDictionary(i => i.Id);

        // Rule 20: SubPlan reference validation (Warning)
        ValidateSubPlans(plan, warnings);

        var referencedItems = new HashSet<string>();

        for (int idx = 0; idx < plan.Items.Count; idx++)
        {
            var item = plan.Items[idx];
            var loc = !string.IsNullOrWhiteSpace(item.Id) ? $"item.{item.Id}" : $"plan/{idx}";

            // Rule 15: Operator existence check (Error)
            var op = operatorPool.GetById(item.OperatorId);
            if (op is null && !string.IsNullOrWhiteSpace(item.OperatorId))
            {
                errors.Add(NewError(ErrorCode.CfgOperatorNotFound,
                    $"Operator '{item.OperatorId}' not found in operator pool.", loc));
            }

            // Rule 16: Input binding completeness (Error) — Upstream SourceId must point to existing item
            if (item.Inputs is not null)
            {
                foreach (var (inputKey, binding) in item.Inputs)
                {
                    if (binding.Type == BindingType.Upstream)
                    {
                        referencedItems.Add(binding.SourceId);

                        if (!string.IsNullOrWhiteSpace(binding.SourceId) && !itemMap.ContainsKey(binding.SourceId))
                        {
                            errors.Add(NewError(ErrorCode.CfgBindingIncomplete,
                                $"Upstream binding '{inputKey}' in item '{item.Id}' references non-existent item '{binding.SourceId}'.", loc));
                        }
                    }
                }
            }

            // Rule 18: Parameter boundary validation
            if (item.Parameters.Count > 0)
            {
                if (op is not null)
                {
                    ValidateParameterConstraints(item, op.Metadata, errors, warnings);
                }
                else
                {
                    warnings.Add(NewWarning(ErrorCode.CfgOperatorNotFound,
                        $"Cannot validate parameters for item '{item.Id}': operator '{item.OperatorId}' not found.", loc));
                }
            }
        }

        // Rule 19: Output binding completeness for non-final items (Warning)
        ValidateOutputBindings(plan.Items, referencedItems, warnings);

        // Rule 21: CRS consistency pre-check (Warning)
        CrsPreCheck(plan, warnings);
    }

    // ---- Rule 17: DAG cycle detection (DFS three-color marking) ----

    private static void ValidateDagNoCycle(
        IReadOnlyList<AnalysisItem> items,
        Dictionary<string, AnalysisItem> itemMap,
        List<ValidationError> errors)
    {
        var color = new Dictionary<string, int>(itemMap.Count); // 0 = unvisited, 1 = visiting, 2 = completed

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
                continue;

            if (!color.ContainsKey(item.Id))
            {
                var cyclePath = new List<string>();
                if (DfsCycle(item.Id, color, itemMap, cyclePath))
                {
                    cyclePath.Insert(0, item.Id);
                    errors.Add(NewError(ErrorCode.CfgDagCycle,
                        $"Dependency cycle detected: {string.Join(" → ", cyclePath)}.", $"item.{item.Id}"));
                    return; // Report first cycle only
                }
            }
        }
    }

    private static bool DfsCycle(
        string nodeId,
        Dictionary<string, int> color,
        Dictionary<string, AnalysisItem> itemMap,
        List<string> path)
    {
        color[nodeId] = 1; // visiting

        if (itemMap.TryGetValue(nodeId, out var item) && item.Inputs is not null)
        {
            foreach (var binding in item.Inputs.Values)
            {
                if (binding.Type != BindingType.Upstream)
                    continue;

                var neighborId = binding.SourceId;
                if (string.IsNullOrWhiteSpace(neighborId) || !itemMap.ContainsKey(neighborId))
                    continue;

                if (!color.TryGetValue(neighborId, out var c))
                    c = 0;

                if (c == 1)
                {
                    // Found a back edge — cycle
                    return true;
                }

                if (c == 0)
                {
                    path.Add(neighborId);
                    if (DfsCycle(neighborId, color, itemMap, path))
                        return true;
                    path.RemoveAt(path.Count - 1);
                }
            }
        }

        color[nodeId] = 2; // completed
        return false;
    }

    // ---- Rule 18: Parameter constraint validation ----

    private static void ValidateParameterConstraints(
        AnalysisItem item, OperatorMetadata metadata,
        List<ValidationError> errors, List<ValidationError> warnings)
    {
        var paramDefMap = metadata.Parameters.ToDictionary(p => p.Name);

        foreach (var (paramName, paramValue) in item.Parameters)
        {
            if (!paramDefMap.TryGetValue(paramName, out var def) || def.Constraint is null)
                continue;

            var constraint = def.Constraint;

            // Numeric bounds
            if (constraint.MinValue is not null || constraint.MaxValue is not null)
            {
                var numVal = TryToDouble(paramValue);
                if (numVal.HasValue)
                {
                    if (constraint.MinValue.HasValue && numVal.Value < constraint.MinValue.Value)
                        errors.Add(NewError(ErrorCode.CfgParamOutOfRange,
                            $"Parameter '{paramName}' of item '{item.Id}' value {numVal.Value} below minimum {constraint.MinValue.Value}.", $"item.{item.Id}"));

                    if (constraint.MaxValue.HasValue && numVal.Value > constraint.MaxValue.Value)
                        errors.Add(NewError(ErrorCode.CfgParamOutOfRange,
                            $"Parameter '{paramName}' of item '{item.Id}' value {numVal.Value} exceeds maximum {constraint.MaxValue.Value}.", $"item.{item.Id}"));
                }
            }

            // Pattern validation
            if (constraint.Pattern is not null && paramValue is string strVal)
            {
                try
                {
                    if (!Regex.IsMatch(strVal, constraint.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(500)))
                        errors.Add(NewError(ErrorCode.CfgParamOutOfRange,
                            $"Parameter '{paramName}' of item '{item.Id}' value '{strVal}' does not match pattern '{constraint.Pattern}'.", $"item.{item.Id}"));
                }
                catch (RegexParseException)
                {
                    // Silently skip invalid regex patterns in constraints
                }
                catch (RegexMatchTimeoutException)
                {
                    warnings.Add(NewWarning(ErrorCode.CfgParamOutOfRange,
                        $"Parameter '{paramName}' of item '{item.Id}' pattern validation timed out for value '{strVal}'.", $"item.{item.Id}"));
                }
            }

            // AllowedValues validation
            if (constraint.AllowedValues is { Count: > 0 })
            {
                var allowedStr = paramValue?.ToString();
                if (allowedStr is not null && !constraint.AllowedValues.Contains(allowedStr))
                    errors.Add(NewError(ErrorCode.CfgParamOutOfRange,
                        $"Parameter '{paramName}' of item '{item.Id}' value '{allowedStr}' is not in allowed values: [{string.Join(", ", constraint.AllowedValues)}].", $"item.{item.Id}"));
            }
        }
    }

    // ---- Rule 19: Output binding completeness for non-final items ----

    private static void ValidateOutputBindings(
        IReadOnlyList<AnalysisItem> items,
        HashSet<string> referencedItemIds,
        List<ValidationError> warnings)
    {
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
                continue;

            // A non-final item is one that is referenced as upstream by another item
            if (referencedItemIds.Contains(item.Id))
            {
                var output = item.Output;
                if (output is null || !output.IsIntermediate)
                {
                    warnings.Add(NewWarning(ErrorCode.CfgSchemaInvalid,
                        $"Non-final item '{item.Id}' (referenced by other items) should have OutputBinding with IsIntermediate=true or a non-empty AdapterType.", $"item.{item.Id}"));
                }
            }
        }
    }

    // ---- Rule 20: SubPlan reference validation ----

    private static void ValidateSubPlans(AnalysisPlan plan, List<ValidationError> warnings)
    {
        if (plan.SubPlans is null || plan.SubPlans.Count == 0)
            return;

        var subPlanIds = new HashSet<string>();

        for (int i = 0; i < plan.SubPlans.Count; i++)
        {
            var subPlan = plan.SubPlans[i];
            var loc = $"subPlan/{i}";

            if (string.IsNullOrWhiteSpace(subPlan?.Id))
            {
                warnings.Add(NewWarning(ErrorCode.CfgSchemaInvalid,
                    $"SubPlan at index {i} has null, empty, or whitespace ID.", loc));
            }
            else if (!subPlanIds.Add(subPlan.Id))
            {
                warnings.Add(NewWarning(ErrorCode.CfgSchemaInvalid,
                    $"Duplicate SubPlan ID '{subPlan.Id}' found in plan.", loc));
            }
        }
    }

    // ---- Rule 21: CRS consistency pre-check ----

    private static void CrsPreCheck(AnalysisPlan plan, List<ValidationError> warnings)
    {
        // CRS consistency check requires source metadata inspection at execution time.
        // Static validation cannot determine CRS without loading actual data sources.
    }

    // ===== Helpers =====

    /// <summary>
    /// Attempts to convert a parameter value to <see cref="double"/>.
    /// Handles <see cref="int"/>, <see cref="long"/>, <see cref="double"/>, <see cref="float"/>,
    /// numeric <see cref="string"/>, and <see cref="JsonElement"/> (number or string).
    /// </summary>
    private static double? TryToDouble(object? value)
    {
        return value switch
        {
            null => null,
            double d => d,
            int i => i,
            long l => l,
            float f => f,
            short s => s,
            byte b => b,
            string s => double.TryParse(s, out var parsed) ? parsed : null,
            JsonElement je => je.ValueKind switch
            {
                JsonValueKind.Number when je.TryGetDouble(out var jd) => jd,
                JsonValueKind.String when double.TryParse(je.GetString(), out var js) => js,
                _ => null
            },
            _ => null
        };
    }

    private static ValidationError NewError(string code, string message, string? location) =>
        new() { Severity = ValidationSeverity.Error, Code = code, Message = message, Location = location };

    private static ValidationError NewWarning(string code, string message, string? location) =>
        new() { Severity = ValidationSeverity.Warning, Code = code, Message = message, Location = location };
}
