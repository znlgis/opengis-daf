using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using OpenGIS.Utils.Geometry;
using OpenGisDAF.Adapters;
using OpenGisDAF.Adapters.Utilities;
using OpenGisDAF.Core;
using ExecutionContext = OpenGisDAF.Core.ExecutionContext;

namespace OpenGisDAF.Operators;

public sealed class BufferOperator : IOperator
{
    public OperatorMetadata Metadata { get; } = new()
    {
        Id = "buffer",
        Name = "缓冲区分析",
        Category = "空间运算",
        Description = "对输入的每个几何要素创建指定距离的缓冲区多边形。",
        Tags = ["buffer", "缓冲区", "空间分析"],
        Version = "1.0.0",
        Parameters =
        [
            new ParameterDefinition
            {
                Name = "distance",
                Type = "double",
                Required = true,
                Description = "缓冲距离（单位与坐标系一致）。"
            }
        ],
        InputSchema = new InputSchema
        {
            RequiredGeometryType = null,
            Description = "源要素集合，支持任意几何类型。"
        },
        OutputSchema = new OutputSchema
        {
            ProducedGeometryType = null,
            Description = "输出为输入几何对应类型的多边形缓冲区结果。"
        }
    };

    public ValidationResult Validate(AnalysisItem config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<ValidationError>();
        var warnings = new List<ValidationError>();

        if (!config.Parameters.TryGetValue("distance", out var distValue) || distValue is null)
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgParamOutOfRange,
                Message = "缺少必需参数 'distance'。"
            });
        }
        else if (double.TryParse(distValue?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var distance))
        {
            if (distance <= 0)
            {
                warnings.Add(new ValidationError
                {
                    Severity = ValidationSeverity.Warning,
                    Code = ErrorCode.CfgParamOutOfRange,
                    Message = $"缓冲距离为 {distance}，小于等于零可能导致空结果。"
                });
            }
        }
        else
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgParamOutOfRange,
                Message = $"参数 'distance' 值 '{distValue}' 不是有效的数值。"
            });
        }

        if (!config.Inputs.ContainsKey("source"))
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgBindingIncomplete,
                Message = "缺少必需输入 'source'。"
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

        try
        {
            if (!parameters.TryGetValue("distance", out var distObj) ||
                !double.TryParse(distObj?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var distance))
            {
                return FailureResult(
                    sw.Elapsed,
                    ErrorCode.CfgParamOutOfRange,
                    "参数 'distance' 缺失或不是有效的数值。");
            }

            if (!inputs.TryGetValue("source", out var source))
            {
                return FailureResult(
                    sw.Elapsed,
                    ErrorCode.CfgBindingIncomplete,
                    "未找到输入 'source'。");
            }

            context.Logger.LogInformation(
                "[Buffer] 开始缓冲区分析，距离={Distance}，要素数约={Count}",
                distance, await source.GetFeatureCountAsync());

            var results = new List<IFeature>();

            await foreach (var feature in source.GetFeaturesAsync(cancellationToken: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var srcWkt = WktConverter.ToWkt(feature.Geometry);
                    var bufferedWkt = GeometryUtil.BufferWkt(srcWkt, distance);
                    var bufferedGeom = WktConverter.FromWkt(bufferedWkt);

                    results.Add(new SimpleFeature(
                        feature.Id,
                        bufferedGeom,
                        feature.Attributes));
                }
                catch (Exception ex)
                {
                    context.Logger.LogWarning(ex,
                        "[Buffer] 要素缓冲计算异常: feature={FeatureId}",
                        feature.Id);
                }
            }

            context.Logger.LogInformation(
                "[Buffer] 完成缓冲区分析，输出要素数={Count}", results.Count);

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
            return FailureResult(
                sw.Elapsed,
                ErrorCode.RtCancelled,
                "缓冲区分析已取消。");
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "[Buffer] 执行失败");
            return FailureResult(
                sw.Elapsed,
                ErrorCode.RtUnexpected,
                ex.Message);
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

}
