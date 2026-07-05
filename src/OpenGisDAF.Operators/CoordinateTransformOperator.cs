using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using OpenGisDAF.Adapters;
using OpenGisDAF.Adapters.Utilities;
using OpenGisDAF.Core;
using OpenGIS.Utils.Engine.Util;
using ExecutionContext = OpenGisDAF.Core.ExecutionContext;

namespace OpenGisDAF.Operators;

public sealed class CoordinateTransformOperator : IOperator
{
    public OperatorMetadata Metadata { get; } = new()
    {
        Id = "coordinate_transform",
        Name = "坐标系转换",
        Category = "格式转换",
        Description = "将要素从源坐标系转换到目标坐标系",
        Version = "1.0.0",
        Tags = ["坐标系", "转换", "CRS", "EPSG"],
        Parameters =
        [
            new ParameterDefinition
            {
                Name = "source_epsg",
                Type = "int",
                Required = true,
                Description = "源坐标系 EPSG 代码"
            },
            new ParameterDefinition
            {
                Name = "target_epsg",
                Type = "int",
                Required = true,
                Description = "目标坐标系 EPSG 代码"
            }
        ],
        InputSchema = new InputSchema
        {
            RequiredGeometryType = null,
            Description = "待转换的要素图层"
        },
        OutputSchema = new OutputSchema
        {
            ProducedGeometryType = null,
            Description = "转换后的要素图层"
        }
    };

    public ValidationResult Validate(AnalysisItem config)
    {
        var errors = new List<ValidationError>();

        if (!config.Inputs.ContainsKey("source"))
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgBindingIncomplete,
                Message = "缺少输入 'source'"
            });
        }

        var sourceEpsg = GetIntParam(config.Parameters, "source_epsg");
        if (sourceEpsg is null || sourceEpsg <= 0)
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgParamOutOfRange,
                Message = "参数 'source_epsg' 必须为正整数"
            });
        }

        var targetEpsg = GetIntParam(config.Parameters, "target_epsg");
        if (targetEpsg is null || targetEpsg <= 0)
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgParamOutOfRange,
                Message = "参数 'target_epsg' 必须为正整数"
            });
        }

        return new ValidationResult
        {
            Errors = errors
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
            if (!inputs.TryGetValue("source", out var source))
                return Fail(ErrorCode.CfgBindingIncomplete, "输入 'source' 未提供", sw.Elapsed, logs);

            var sourceEpsg = GetIntParam(parameters, "source_epsg");
            if (sourceEpsg is null || sourceEpsg <= 0)
                return Fail(ErrorCode.CfgParamOutOfRange, "参数 'source_epsg' 必须为正整数", sw.Elapsed, logs);

            var targetEpsg = GetIntParam(parameters, "target_epsg");
            if (targetEpsg is null || targetEpsg <= 0)
                return Fail(ErrorCode.CfgParamOutOfRange, "参数 'target_epsg' 必须为正整数", sw.Elapsed, logs);

            var transformedFeatures = new List<IFeature>();
            var featureCount = 0;

            await foreach (var feature in source.GetFeaturesAsync(cancellationToken: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var wkt = WktConverter.ToWkt(feature.Geometry);
                    var transformedWkt = CrsUtil.Transform(wkt, sourceEpsg.Value, targetEpsg.Value);
                    var newGeometry = WktConverter.FromWkt(transformedWkt);

                    transformedFeatures.Add(new Feature(feature.Id, newGeometry, feature.Attributes));
                    featureCount++;
                }
                catch (Exception ex)
                {
                    context.Logger.LogWarning(ex,
                        "[CoordinateTransform] 要素坐标转换异常: feature={FeatureId}",
                        feature.Id);
                }
            }

            var output = new InMemoryFeatureSource(transformedFeatures);

            logs.Add(new ExecutionLogEntry
            {
                ExecutionId = context.ExecutionId,
                ItemId = string.Empty,
                OperatorId = Metadata.Id,
                Level = LogLevel.Information,
                Message = $"成功转换 {featureCount} 个要素 (EPSG:{sourceEpsg} -> EPSG:{targetEpsg})"
            });

            return new ExecutionResult
            {
                Status = ExecutionStatus.Success,
                Outputs = new Dictionary<string, object?> { ["output"] = output },
                Logs = logs,
                Elapsed = sw.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            return new ExecutionResult
            {
                Status = ExecutionStatus.Canceled,
                ErrorCode = ErrorCode.RtCancelled,
                Elapsed = sw.Elapsed,
                Logs = logs
            };
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "坐标系转换失败");
            return Fail(ErrorCode.RtUnexpected, $"坐标系转换失败: {ex.Message}", sw.Elapsed, logs);
        }
    }

    private static int? GetIntParam(IReadOnlyDictionary<string, object?> parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var val)) return null;

        return val switch
        {
            int i => i,
            long l => l > int.MaxValue ? null : (int)l,
            double d => d is >= int.MinValue and <= int.MaxValue ? (int)d : null,
            string s when int.TryParse(s, out var i) => i,
            _ => null
        };
    }

    private static ExecutionResult Fail(string code, string message, TimeSpan elapsed, List<ExecutionLogEntry> logs)
    {
        return new ExecutionResult
        {
            Status = ExecutionStatus.Failed,
            ErrorCode = code,
            ErrorMessage = message,
            Elapsed = elapsed,
            Logs = logs
        };
    }

    private sealed record Feature(
        string Id,
        Geometry Geometry,
        IReadOnlyDictionary<string, object?> Attributes
    ) : IFeature;
}
