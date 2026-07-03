using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;
using OpenGisDAF.Adapters;
using OpenGisDAF.Core;
using ExecutionContext = OpenGisDAF.Core.ExecutionContext;

namespace OpenGisDAF.Operators;

public sealed class GeometryValidityChecker : IOperator
{
    private const string InputName = "source";
    private const string ParamQcMode = "qc_mode";
    private const string OutputKeyPassed = "result";
    private const string OutputKeyIssues = "issues";

    public OperatorMetadata Metadata { get; } = new()
    {
        Id = "geometry_validity_checker",
        Name = "几何有效性检查",
        Category = "质检规则",
        Description = "检查要素几何是否符合 OGC 有效性规则。",
        Tags = ["质检", "几何", "有效性"],
        Version = "1.0.0",
        Parameters = [],
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

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
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

        var startTime = DateTimeOffset.UtcNow;
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

                var featureIssues = CheckFeature(feature, context);
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
                Elapsed = DateTimeOffset.UtcNow - startTime
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
                Elapsed = DateTimeOffset.UtcNow - startTime
            };
        }

        logger.LogInformation(
            "{OperatorId} 检查完成: 共 {Total} 个要素，通过 {Passed}，未通过 {Failed}，警告 {Warning}",
            Metadata.Id, featureCount, passedCount, failedCount, warningCount);

        var elapsed = DateTimeOffset.UtcNow - startTime;

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
                [OutputKeyPassed] = new InMemoryFeatureSource(passedFeatures, $"gvc_passed_{context.ExecutionId}")
            },
            Elapsed = elapsed
        };
    }

    private static List<IssueRecord> CheckFeature(IFeature feature, ExecutionContext context)
    {
        var issues = new List<IssueRecord>();
        var geom = feature.Geometry;

        if (geom is null || geom.IsEmpty)
        {
            issues.Add(new IssueRecord
            {
                PlanId = context.PlanId,
                ExecutionId = context.ExecutionId,
                ItemId = context.ExecutionId,
                FeatureId = feature.Id,
                IssueType = "GEOM_EMPTY",
                Severity = IssueSeverity.Error,
                Description = "要素几何为空",
                ContextData = new Dictionary<string, object?>
                {
                    ["feature_id"] = feature.Id
                }
            });
            return issues;
        }

        if (!geom.IsValid)
        {
            var errorMsg = new IsValidOp(geom).ValidationError;
            var reason = errorMsg?.Message ?? string.Empty;

            issues.Add(new IssueRecord
            {
                PlanId = context.PlanId,
                ExecutionId = context.ExecutionId,
                ItemId = context.ExecutionId,
                FeatureId = feature.Id,
                IssueType = "GEOM_INVALID",
                Severity = IssueSeverity.Error,
                Description = string.IsNullOrWhiteSpace(reason)
                    ? "几何无效"
                    : $"几何无效: {reason}",
                ViolationGeometry = geom,
                ContextData = new Dictionary<string, object?>
                {
                    ["feature_id"] = feature.Id,
                    ["geometry_type"] = geom.GeometryType
                }
            });
        }

        if (IsLineal(geom) && !geom.IsSimple)
        {
            issues.Add(new IssueRecord
            {
                PlanId = context.PlanId,
                ExecutionId = context.ExecutionId,
                ItemId = context.ExecutionId,
                FeatureId = feature.Id,
                IssueType = "GEOM_NOT_SIMPLE",
                Severity = IssueSeverity.Warning,
                Description = "线几何存在自相交（非简单几何），但仍满足有效性规则",
                ViolationGeometry = geom,
                ContextData = new Dictionary<string, object?>
                {
                    ["feature_id"] = feature.Id,
                    ["geometry_type"] = geom.GeometryType
                }
            });
        }

        return issues;
    }

    private static bool IsLineal(Geometry geom)
    {
        return geom.OgcGeometryType is OgcGeometryType.LineString
            or OgcGeometryType.MultiLineString;
    }
}
