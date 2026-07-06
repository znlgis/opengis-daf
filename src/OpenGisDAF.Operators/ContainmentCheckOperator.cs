using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using OpenGIS.Utils.Geometry;
using OpenGisDAF.Adapters;
using OpenGisDAF.Adapters.Utilities;
using OpenGisDAF.Core;
using ExecutionContext = OpenGisDAF.Core.ExecutionContext;

namespace OpenGisDAF.Operators;

public sealed class ContainmentCheckOperator : IOperator
{
    public OperatorMetadata Metadata { get; } = new()
    {
        Id = "containment_check",
        Name = "包含检查",
        Category = "空间关系",
        Description = "判断 source 要素是否包含（或位于）target 要素内部。支持分析模式和QC模式。",
        Tags = ["contain", "within", "spatial-relation", "qc"],
        Version = "1.0.0",
        MinFrameworkVersion = "1.0",
        Parameters =
        [
            new ParameterDefinition
            {
                Name = "relationship",
                Type = "string",
                Required = false,
                DefaultValue = "contains",
                Description = "空间关系类型：contains（source包含target）或 within（source位于target内部）。",
                Constraint = new ParameterConstraint
                {
                    AllowedValues = ["contains", "within"]
                }
            }
        ],
        InputSchema = new InputSchema
        {
            Description = "需要两个要素源：source（被检查要素集）和 target（参考要素集）。"
        },
        OutputSchema = new OutputSchema
        {
            ProducedFields = [],
            ProducedGeometryType = null,
            Description = "包含满足空间关系条件的要素对的要素集（分析模式）或问题记录要素集（QC模式）。"
        }
    };

    public ValidationResult Validate(AnalysisItem config)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationError>();

        if (!config.Inputs.ContainsKey("source"))
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgSchemaInvalid,
                Message = "必须提供 'source' 输入要素源。"
            });
        }

        if (!config.Inputs.ContainsKey("target"))
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgSchemaInvalid,
                Message = "必须提供 'target' 输入要素源。"
            });
        }

        var relationship = GetRelationship(config.Parameters);
        if (relationship != "contains" && relationship != "within")
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgSchemaInvalid,
                Message = "参数 'relationship' 必须为 'contains' 或 'within'。"
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
        var sw = Stopwatch.StartNew();
        var logs = new List<ExecutionLogEntry>();

        try
        {
            var relationship = GetRelationship(parameters);
            var qcMode = GetQcMode(parameters);

            context.Logger.LogInformation(
                "[ContainmentCheck] 开始包含检查: QcMode={QcMode}, Relationship={Relationship}",
                qcMode, relationship);

            if (!inputs.TryGetValue("source", out var sourceSource))
            {
                return FailResult("source 输入要素源未提供。", sw.Elapsed, logs);
            }

            if (!inputs.TryGetValue("target", out var targetSource))
            {
                return FailResult("target 输入要素源未提供。", sw.Elapsed, logs);
            }

            // 收集 source 要素
            var sourceFeatures = new List<IFeature>();
            await foreach (var feature in sourceSource.GetFeaturesAsync(cancellationToken: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                sourceFeatures.Add(feature);
            }

            context.Logger.LogInformation("[ContainmentCheck] 加载 source 要素: {Count}", sourceFeatures.Count);

            // 收集 target 要素
            var targetFeatures = new List<IFeature>();
            await foreach (var feature in targetSource.GetFeaturesAsync(cancellationToken: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                targetFeatures.Add(feature);
            }

            context.Logger.LogInformation("[ContainmentCheck] 加载 target 要素: {Count}", targetFeatures.Count);

            // 执行空间分析
            var outputFeatures = relationship switch
            {
                "contains" => CheckContains(sourceFeatures, targetFeatures, qcMode, context, cancellationToken),
                "within" => CheckWithin(sourceFeatures, targetFeatures, qcMode, context, cancellationToken),
                _ => new List<IFeature>()
            };

            var resultSource = new InMemoryFeatureSource(outputFeatures);
            var elapsed = sw.Elapsed;

            context.Logger.LogInformation(
                "[ContainmentCheck] 完成: 产出 {Count} 条结果, 耗时 {Elapsed}ms",
                outputFeatures.Count, elapsed.TotalMilliseconds);

            return new ExecutionResult
            {
                Status = ExecutionStatus.Success,
                Outputs = new Dictionary<string, object?>
                {
                    ["output"] = resultSource
                },
                Elapsed = elapsed,
                Logs = logs
            };
        }
        catch (OperationCanceledException)
        {
            context.Logger.LogWarning("[ContainmentCheck] 操作已取消");
            return new ExecutionResult
            {
                Status = ExecutionStatus.Canceled,
                ErrorCode = ErrorCode.RtCancelled,
                ErrorMessage = "操作已取消。",
                Elapsed = sw.Elapsed,
                Logs = logs
            };
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "[ContainmentCheck] 执行异常");
            return new ExecutionResult
            {
                Status = ExecutionStatus.Failed,
                ErrorCode = ErrorCode.RtUnexpected,
                ErrorMessage = ex.Message,
                Elapsed = sw.Elapsed,
                Logs = logs
            };
        }
    }

    private static string GetRelationship(IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters.TryGetValue("relationship", out var val) && val is string s && !string.IsNullOrWhiteSpace(s))
            return s;

        return "contains";
    }

    private static bool GetQcMode(IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters.TryGetValue("_qc_mode", out var val) && val is bool b)
            return b;

        return false;
    }

    private List<IFeature> CheckContains(
        List<IFeature> sourceFeatures,
        List<IFeature> targetFeatures,
        bool qcMode,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        var results = new List<IFeature>();
        long hits = 0;

        foreach (var src in sourceFeatures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var srcWkt = WktConverter.ToWkt(src.Geometry);

            foreach (var tgt in targetFeatures)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tgtWkt = WktConverter.ToWkt(tgt.Geometry);

                bool contains;
                try
                {
                    contains = GeometryUtil.ContainsWkt(srcWkt, tgtWkt);
                }
                catch (Exception ex)
                {
                    context.Logger.LogWarning(ex,
                        "[ContainmentCheck] 空间计算异常: source={SrcId}, target={TgtId}",
                        src.Id, tgt.Id);
                    continue;
                }

                if (!contains) continue;

                hits++;

                if (qcMode)
                {
                    results.Add(CreateQcFeature(src, tgt, srcWkt, tgtWkt, "contains", context));
                }
                else
                {
                    results.Add(CreatePairFeature(src, tgt, srcWkt, tgtWkt, "contains"));
                }
            }
        }

        context.Logger.LogInformation(
            "[ContainmentCheck] 包含检查统计: 发现 {Hits} 处包含关系",
            hits);

        return results;
    }

    private List<IFeature> CheckWithin(
        List<IFeature> sourceFeatures,
        List<IFeature> targetFeatures,
        bool qcMode,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        var results = new List<IFeature>();
        long hits = 0;

        foreach (var src in sourceFeatures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var srcWkt = WktConverter.ToWkt(src.Geometry);

            foreach (var tgt in targetFeatures)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tgtWkt = WktConverter.ToWkt(tgt.Geometry);

                // "A within B" is equivalent to "B contains A"
                bool within;
                try
                {
                    within = GeometryUtil.ContainsWkt(tgtWkt, srcWkt);
                }
                catch (Exception ex)
                {
                    context.Logger.LogWarning(ex,
                        "[ContainmentCheck] 空间计算异常: source={SrcId}, target={TgtId}",
                        src.Id, tgt.Id);
                    continue;
                }

                if (!within) continue;

                hits++;

                if (qcMode)
                {
                    results.Add(CreateQcFeature(src, tgt, srcWkt, tgtWkt, "within", context));
                }
                else
                {
                    results.Add(CreatePairFeature(src, tgt, srcWkt, tgtWkt, "within"));
                }
            }
        }

        context.Logger.LogInformation(
            "[ContainmentCheck] Within检查统计: 发现 {Hits} 处被包含关系",
            hits);

        return results;
    }

    private static IFeature CreatePairFeature(IFeature src, IFeature tgt, string srcWkt, string tgtWkt, string relation)
    {
        return new SimpleFeature(
            Id: $"{src.Id}|{tgt.Id}",
            Geometry: src.Geometry,
            Attributes: new Dictionary<string, object?>
            {
                ["source_id"] = src.Id,
                ["target_id"] = tgt.Id,
                ["source_wkt"] = srcWkt,
                ["target_wkt"] = tgtWkt,
                ["relation"] = relation
            });
    }

    private static IFeature CreateQcFeature(IFeature src, IFeature tgt, string srcWkt, string tgtWkt,
        string issueType, ExecutionContext context)
    {
        var description = issueType == "contains"
            ? $"要素 {src.Id} 包含要素 {tgt.Id}"
            : $"要素 {src.Id} 位于要素 {tgt.Id} 内部";

        return new SimpleFeature(
            Id: Guid.NewGuid().ToString(),
            Geometry: src.Geometry,
            Attributes: new Dictionary<string, object?>
            {
                ["issue_type"] = issueType,
                ["severity"] = "Error",
                ["source_feature_id"] = src.Id,
                ["target_feature_id"] = tgt.Id,
                ["source_wkt"] = srcWkt,
                ["target_wkt"] = tgtWkt,
                ["description"] = description,
                ["plan_id"] = context.PlanId,
                ["execution_id"] = context.ExecutionId
            });
    }

    private static ExecutionResult FailResult(string message, TimeSpan elapsed, IReadOnlyList<ExecutionLogEntry> logs)
    {
        return new ExecutionResult
        {
            Status = ExecutionStatus.Failed,
            ErrorCode = ErrorCode.CfgSchemaInvalid,
            ErrorMessage = message,
            Elapsed = elapsed,
            Logs = logs
        };
    }

}
