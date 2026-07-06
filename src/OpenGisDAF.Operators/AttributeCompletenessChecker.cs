using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenGisDAF.Adapters;
using OpenGisDAF.Core;
using ExecutionContext = OpenGisDAF.Core.ExecutionContext;

namespace OpenGisDAF.Operators;

public sealed class AttributeCompletenessChecker : IOperator
{
    private const string InputName = "source";
    private const string ParamRequiredFields = "required_fields";
    private const string ParamQcMode = "_qc_mode";
    private const string OutputKeyPassed = "output";
    private const string OutputKeyIssues = "issues";

    public OperatorMetadata Metadata { get; } = new()
    {
        Id = "attribute_completeness_checker",
        Name = "属性完整性检查",
        Category = "质检规则",
        Description = "检查要素的必填字段是否为空值或空字符串。",
        Tags = ["质检", "属性", "完整性"],
        Version = "1.0.0",
        Parameters =
        [
            new ParameterDefinition
            {
                Name = ParamRequiredFields,
                Type = "string",
                Required = true,
                Description = "逗号分隔的必填字段名，如 \"name,address,area\""
            }
        ],
        InputSchema = new InputSchema
        {
            Description = "待检查的要素源"
        },
        OutputSchema = new OutputSchema
        {
            ProducedFields = [],
            Description = "QC模式下输出 IssueRecord 列表；非QC模式下输出通过检查的要素源"
        }
    };

    public ValidationResult Validate(AnalysisItem config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationError>();

        if (!config.Inputs.ContainsKey(InputName))
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgBindingIncomplete,
                Message = $"缺少必需输入 '{InputName}'"
            });
        }

        if (!config.Parameters.TryGetValue(ParamRequiredFields, out var raw) ||
            raw is not string fieldsStr || string.IsNullOrWhiteSpace(fieldsStr))
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgParamOutOfRange,
                Message = $"缺少必需参数 '{ParamRequiredFields}'，或值为空"
            });
        }

        return new ValidationResult
        {
            Errors = errors,
            Warnings = warnings
        };
    }

    public async Task<ExecutionResult> ExecuteAsync(
        IReadOnlyDictionary<string, IFeatureSource> inputs,
        IReadOnlyDictionary<string, object?> parameters,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(context);

        var sw = Stopwatch.StartNew();
        var logger = context.Logger;

        if (!inputs.TryGetValue(InputName, out var source))
        {
            return new ExecutionResult
            {
                Status = ExecutionStatus.Failed,
                ErrorCode = ErrorCode.CfgBindingIncomplete,
                ErrorMessage = "缺少输入 'source'"
            };
        }

        var fieldsStr = parameters.TryGetValue(ParamRequiredFields, out var raw) && raw is string s
            ? s
            : string.Empty;

        if (string.IsNullOrWhiteSpace(fieldsStr))
        {
            return new ExecutionResult
            {
                Status = ExecutionStatus.Failed,
                ErrorCode = ErrorCode.CfgParamOutOfRange,
                ErrorMessage = $"参数 '{ParamRequiredFields}' 为空或缺失"
            };
        }

        var requiredFields = fieldsStr
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requiredFields.Count == 0)
        {
            return new ExecutionResult
            {
                Status = ExecutionStatus.Failed,
                ErrorCode = ErrorCode.CfgParamOutOfRange,
                ErrorMessage = $"参数 '{ParamRequiredFields}' 未包含有效字段名"
            };
        }

        var qcMode = parameters.TryGetValue(ParamQcMode, out var qcObj) && qcObj is true;

        var issues = new List<IssueRecord>();
        var passedFeatures = new List<IFeature>();
        var featureCount = 0L;
        var passedCount = 0L;
        var failedCount = 0L;
        var warningCount = 0L;

        try
        {
            await foreach (var feature in source.GetFeaturesAsync(cancellationToken: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                featureCount++;

                var featureIssues = CheckFeature(feature, requiredFields, context);
                if (featureIssues.Count > 0)
                {
                    var hasError = featureIssues.Any(i => i.Severity == IssueSeverity.Error);
                    if (hasError)
                    {
                        failedCount++;
                    }
                    else
                    {
                        warningCount++;
                        passedCount++;
                        passedFeatures.Add(feature);
                    }

                    issues.AddRange(featureIssues);
                }
                else
                {
                    passedCount++;
                    passedFeatures.Add(feature);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return new ExecutionResult
            {
                Status = ExecutionStatus.Canceled,
                ErrorCode = ErrorCode.RtCancelled,
                Elapsed = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "算子执行失败: {OperatorId}", Metadata.Id);
            return new ExecutionResult
            {
                Status = ExecutionStatus.Failed,
                ErrorCode = ErrorCode.RtUnexpected,
                ErrorMessage = ex.Message,
                Elapsed = sw.Elapsed
            };
        }

        logger.LogInformation(
            "{OperatorId} 检查完成: 共 {Total} 个要素，通过 {Passed}，未通过 {Failed}，警告 {Warning}",
            Metadata.Id, featureCount, passedCount, failedCount, warningCount);

        var elapsed = sw.Elapsed;

        if (qcMode)
        {
            return new ExecutionResult
            {
                Status = ExecutionStatus.Success,
                Outputs = new Dictionary<string, object?>
                {
                    [OutputKeyIssues] = issues
                },
                Elapsed = elapsed
            };
        }

        return new ExecutionResult
        {
            Status = ExecutionStatus.Success,
            Outputs = new Dictionary<string, object?>
            {
                [OutputKeyPassed] = new InMemoryFeatureSource(passedFeatures, $"acc_passed_{context.ExecutionId}")
            },
            Elapsed = elapsed
        };
    }

    private static List<IssueRecord> CheckFeature(
        IFeature feature,
        IReadOnlyList<string> requiredFields,
        ExecutionContext context)
    {
        var issues = new List<IssueRecord>();

        foreach (var field in requiredFields)
        {
            var hasValue = feature.Attributes.TryGetValue(field, out var value);

            if (!hasValue || value is null)
            {
                issues.Add(new IssueRecord
                {
                    PlanId = context.PlanId,
                    ExecutionId = context.ExecutionId,
                    ItemId = context.CurrentItemId,
                    FeatureId = feature.Id,
                    IssueType = "ATTR_MISSING",
                    Severity = IssueSeverity.Error,
                    Description = $"必填字段 '{field}' 缺失（值为 null）",
                    ContextData = new Dictionary<string, object?>
                    {
                        ["field"] = field,
                        ["feature_id"] = feature.Id
                    }
                });
            }
            else if (value is string strValue && strValue.Length == 0)
            {
                issues.Add(new IssueRecord
                {
                    PlanId = context.PlanId,
                    ExecutionId = context.ExecutionId,
                    ItemId = context.CurrentItemId,
                    FeatureId = feature.Id,
                    IssueType = "ATTR_EMPTY",
                    Severity = IssueSeverity.Warning,
                    Description = $"必填字段 '{field}' 为空字符串",
                    ContextData = new Dictionary<string, object?>
                    {
                        ["field"] = field,
                        ["feature_id"] = feature.Id
                    }
                });
            }
        }

        return issues;
    }
}
