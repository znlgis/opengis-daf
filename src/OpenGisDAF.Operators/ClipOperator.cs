using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using OpenGIS.Utils.Geometry;
using OpenGisDAF.Adapters;
using OpenGisDAF.Adapters.Utilities;
using OpenGisDAF.Core;
using ExecutionContext = OpenGisDAF.Core.ExecutionContext;

namespace OpenGisDAF.Operators;

public sealed class ClipOperator : IOperator
{
    public OperatorMetadata Metadata { get; } = new()
    {
        Id = "clip",
        Name = "裁剪分析",
        Category = "空间运算",
        Description = "使用裁剪面要素对源要素进行几何裁剪，输出相交部分。",
        Tags = ["clip", "裁剪", "intersection", "空间分析"],
        Version = "1.0.0",
        Parameters = [],
        InputSchema = new InputSchema
        {
            RequiredGeometryType = null,
            Description = "两个输入：'source'（被裁剪要素）和 'clip'（裁剪面要素）。"
        },
        OutputSchema = new OutputSchema
        {
            ProducedGeometryType = null,
            Description = "输出为源要素与裁剪面相交的几何部分。"
        }
    };

    public ValidationResult Validate(AnalysisItem config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<ValidationError>();

        if (!config.Inputs.ContainsKey("source"))
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgBindingIncomplete,
                Message = "缺少必需输入 'source'。"
            });
        }

        if (!config.Inputs.ContainsKey("clip"))
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgBindingIncomplete,
                Message = "缺少必需输入 'clip'。"
            });
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = []
        };
    }

    public async Task<ExecutionResult> ExecuteAsync(
        IReadOnlyDictionary<string, IFeatureSource> inputs,
        IReadOnlyDictionary<string, object?> parameters,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            if (!inputs.TryGetValue("source", out var source))
            {
                return FailureResult(sw.Elapsed, ErrorCode.CfgBindingIncomplete, "未找到输入 'source'。");
            }

            if (!inputs.TryGetValue("clip", out var clip))
            {
                return FailureResult(sw.Elapsed, ErrorCode.CfgBindingIncomplete, "未找到输入 'clip'。");
            }

            context.Logger.LogInformation(
                "[Clip] 开始裁剪分析，源要素数约={SrcCount}，裁面要素数约={ClipCount}",
                await source.GetFeatureCountAsync(), await clip.GetFeatureCountAsync());

            var clipFeatures = new List<IFeature>();
            await foreach (var cf in clip.GetFeaturesAsync(cancellationToken: cancellationToken))
            {
                clipFeatures.Add(cf);
            }

            var results = new List<IFeature>();
            var seq = 0;

            await foreach (var srcFeature in source.GetFeaturesAsync(cancellationToken: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var srcWkt = WktConverter.ToWkt(srcFeature.Geometry);

                foreach (var clipFeature in clipFeatures)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var clipWkt = WktConverter.ToWkt(clipFeature.Geometry);
                        var intersectedWkt = GeometryUtil.IntersectionWkt(srcWkt, clipWkt);

                        if (string.IsNullOrWhiteSpace(intersectedWkt))
                        {
                            continue;
                        }

                        var intersectedGeom = WktConverter.FromWkt(intersectedWkt);

                        if (intersectedGeom.IsEmpty)
                        {
                            continue;
                        }

                        seq++;
                        var resultId = $"{srcFeature.Id}_clip_{clipFeature.Id}_{seq}";
                        results.Add(new ResultFeature(resultId, intersectedGeom, srcFeature.Attributes));
                    }
                    catch (Exception ex)
                    {
                        context.Logger.LogWarning(ex,
                            "[Clip] 裁剪计算异常: source={SrcId}, clip={ClipId}",
                            srcFeature.Id, clipFeature.Id);
                    }
                }
            }

            context.Logger.LogInformation(
                "[Clip] 完成裁剪分析，输出要素数={Count}", results.Count);

            var output = new InMemoryFeatureSource(results);

            return new ExecutionResult
            {
                Status = ExecutionStatus.Success,
                Outputs = new Dictionary<string, object?>
                {
                    ["output"] = output
                },
                Elapsed = sw.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            return FailureResult(sw.Elapsed, ErrorCode.RtCancelled, "裁剪分析已取消。");
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "[Clip] 执行失败");
            return FailureResult(sw.Elapsed, ErrorCode.RtUnexpected, ex.Message);
        }
    }

    private static ExecutionResult FailureResult(TimeSpan elapsed, string errorCode, string message)
        => new()
        {
            Status = ExecutionStatus.Failed,
            ErrorCode = errorCode,
            ErrorMessage = message,
            Elapsed = elapsed
        };

    private sealed class ResultFeature : IFeature
    {
        public string Id { get; }
        public Geometry Geometry { get; }
        public IReadOnlyDictionary<string, object?> Attributes { get; }

        public ResultFeature(string id, Geometry geometry, IReadOnlyDictionary<string, object?> attributes)
        {
            Id = id;
            Geometry = geometry;
            Attributes = attributes;
        }
    }
}
