using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using OpenGIS.Utils.Geometry;
using OpenGisDAF.Adapters;
using OpenGisDAF.Adapters.Utilities;
using OpenGisDAF.Core;
using ExecutionContext = OpenGisDAF.Core.ExecutionContext;

namespace OpenGisDAF.Operators;

public sealed class IntersectCheckOperator : IOperator
{
    public OperatorMetadata Metadata { get; } = new()
    {
        Id = "intersect_check",
        Name = "相交检查",
        Category = "空间关系",
        Description = "判断两个要素集之间或单个要素集内部的要素是否存在相交关系。支持分析模式和QC模式。",
        Tags = ["intersect", "spatial-relation", "qc"],
        Version = "1.0.0",
        MinFrameworkVersion = "1.0",
        Parameters =
        [
            new ParameterDefinition
            {
                Name = "use_second_input",
                Type = "bool",
                Required = false,
                DefaultValue = true,
                Description = "是否使用第二个输入要素集进行两集合交叉检查。设为 false 时仅检查输入要素自身之间的相交关系。"
            }
        ],
        InputSchema = new InputSchema
        {
            Description = "需要至少一个要素源（source）。当 use_second_input=true 时还需要 target 要素源。"
        },
        OutputSchema = new OutputSchema
        {
            ProducedFields = [],
            ProducedGeometryType = null,
            Description = "包含相交要素对的要素集（分析模式）或问题记录要素集（QC模式）。"
        }
    };

    public ValidationResult Validate(AnalysisItem config)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationError>();

        var useSecondInput = GetUseSecondInput(config.Parameters);

        if (!config.Inputs.ContainsKey("source"))
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgSchemaInvalid,
                Message = "必须提供 'source' 输入要素源。"
            });
        }

        if (useSecondInput && !config.Inputs.ContainsKey("target"))
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgSchemaInvalid,
                Message = "当 use_second_input=true 时必须提供 'target' 输入要素源。"
            });
        }

        if (!useSecondInput && config.Inputs.ContainsKey("target"))
        {
            warnings.Add(new ValidationError
            {
                Severity = ValidationSeverity.Warning,
                Code = ErrorCode.CfgSchemaInvalid,
                Message = "use_second_input=false 时 'target' 输入将被忽略。"
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
            var useSecondInput = GetUseSecondInput(parameters);
            var qcMode = GetQcMode(parameters);

            context.Logger.LogInformation(
                "[IntersectCheck] 开始相交检查: QcMode={QcMode}, UseSecondInput={UseSecondInput}",
                qcMode, useSecondInput);

            // 收集 source 要素
            if (!inputs.TryGetValue("source", out var sourceSource))
            {
                return FailResult("source 输入要素源未提供。", sw.Elapsed, logs);
            }

            var sourceFeatures = new List<IFeature>();
            await foreach (var feature in sourceSource.GetFeaturesAsync(cancellationToken: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                sourceFeatures.Add(feature);
            }

            context.Logger.LogInformation("[IntersectCheck] 加载 source 要素: {Count}", sourceFeatures.Count);

            List<IFeature> targetFeatures;

            if (useSecondInput)
            {
                if (!inputs.TryGetValue("target", out var targetSource))
                {
                    return FailResult("target 输入要素源未提供。", sw.Elapsed, logs);
                }

                targetFeatures = new List<IFeature>();
                await foreach (var feature in targetSource.GetFeaturesAsync(cancellationToken: cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    targetFeatures.Add(feature);
                }

                context.Logger.LogInformation("[IntersectCheck] 加载 target 要素: {Count}", targetFeatures.Count);
            }
            else
            {
                targetFeatures = sourceFeatures;
            }

            // 执行空间分析
            var outputFeatures = useSecondInput
                ? CheckCrossIntersect(sourceFeatures, targetFeatures, qcMode, context, cancellationToken)
                : CheckSelfIntersect(sourceFeatures, qcMode, context, cancellationToken);

            var resultSource = new InMemoryFeatureSource(outputFeatures);
            var elapsed = sw.Elapsed;

            context.Logger.LogInformation(
                "[IntersectCheck] 完成: 产出 {Count} 条结果, 耗时 {Elapsed}ms",
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
            context.Logger.LogWarning("[IntersectCheck] 操作已取消");
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
            context.Logger.LogError(ex, "[IntersectCheck] 执行异常");
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

    private static bool GetUseSecondInput(IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters.TryGetValue("use_second_input", out var val) && val is bool b)
            return b;

        return true;
    }

    private static bool GetQcMode(IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters.TryGetValue("_qc_mode", out var val) && val is bool b)
            return b;

        return false;
    }

    private List<IFeature> CheckCrossIntersect(
        List<IFeature> sourceFeatures,
        List<IFeature> targetFeatures,
        bool qcMode,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        var results = new List<IFeature>();
        long processed = 0;
        long hits = 0;

        var sourceWkts = sourceFeatures.Select(f => WktConverter.ToWkt(f.Geometry)).ToList();
        var targetWkts = targetFeatures.Select(f => WktConverter.ToWkt(f.Geometry)).ToList();

        for (int i = 0; i < sourceFeatures.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed++;

            var src = sourceFeatures[i];
            var srcWkt = sourceWkts[i];

            for (int j = 0; j < targetFeatures.Count; j++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var tgt = targetFeatures[j];
                var tgtWkt = targetWkts[j];

                bool intersects;
                try
                {
                    intersects = GeometryUtil.IntersectsWkt(srcWkt, tgtWkt);
                }
                catch (Exception ex)
                {
                    context.Logger.LogWarning(ex,
                        "[IntersectCheck] 空间计算异常: source={SrcId}, target={TgtId}",
                        src.Id, tgt.Id);
                    continue;
                }

                if (!intersects) continue;

                hits++;

                if (qcMode)
                {
                    results.Add(CreateQcFeature(src, tgt, srcWkt, tgtWkt, "intersect", context));
                }
                else
                {
                    results.Add(CreatePairFeature(src, tgt, srcWkt, tgtWkt));
                }
            }
        }

        context.Logger.LogInformation(
            "[IntersectCheck] 交叉检查统计: 检查 {Processed} 个 source 要素, 发现 {Hits} 处相交",
            processed, hits);

        return results;
    }

    private List<IFeature> CheckSelfIntersect(
        List<IFeature> features,
        bool qcMode,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        var results = new List<IFeature>();
        long hits = 0;

        var wkts = features.Select(f => WktConverter.ToWkt(f.Geometry)).ToList();

        for (int i = 0; i < features.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var src = features[i];
            var srcWkt = wkts[i];

            for (int j = i + 1; j < features.Count; j++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var tgt = features[j];
                var tgtWkt = wkts[j];

                bool intersects;
                try
                {
                    intersects = GeometryUtil.IntersectsWkt(srcWkt, tgtWkt);
                }
                catch (Exception ex)
                {
                    context.Logger.LogWarning(ex,
                        "[IntersectCheck] 空间计算异常: feature_A={IdA}, feature_B={IdB}",
                        src.Id, tgt.Id);
                    continue;
                }

                if (!intersects) continue;

                hits++;

                if (qcMode)
                {
                    results.Add(CreateQcFeature(src, tgt, srcWkt, tgtWkt, "intersect", context));
                }
                else
                {
                    results.Add(CreatePairFeature(src, tgt, srcWkt, tgtWkt));
                }
            }
        }

        context.Logger.LogInformation(
            "[IntersectCheck] 自相交检查统计: 检查 {Count} 个要素, 发现 {Hits} 处相交",
            features.Count, hits);

        return results;
    }

    private static IFeature CreatePairFeature(IFeature src, IFeature tgt, string srcWkt, string tgtWkt)
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
                ["relation"] = "intersect"
            });
    }

    private static IFeature CreateQcFeature(IFeature src, IFeature tgt, string srcWkt, string tgtWkt,
        string issueType, ExecutionContext context)
    {
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
                ["description"] = $"要素 {src.Id} 与要素 {tgt.Id} 相交",
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

    private sealed record SimpleFeature(string Id, Geometry Geometry, IReadOnlyDictionary<string, object?> Attributes) : IFeature;
}
