using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenGisDAF.Core;
using ExecutionContext = OpenGisDAF.Core.ExecutionContext;

namespace OpenGisDAF.SamplePlugin;

/// <summary>
/// 示例插件算子：计算线要素长度（公里）并写入指定字段。
/// 约定输入为米制坐标系（如 EPSG:3857）。
/// </summary>
/// <remarks>
/// 本算子演示编写第三方插件的完整契约：
/// <list type="bullet">
/// <item>只依赖 <c>OpenGisDAF.Core</c> 的公共接口，不引用任何实现层程序集；</item>
/// <item>输出使用自带的 <see cref="PluginFeatureSource"/>（第三方无需引用 Adapters）；</item>
/// <item>不实现 <c>IFeatureSchemaProvider</c>，框架写出时会自动推断字段并集——
/// 第三方算子可以用最小代价接入，需要精确 schema 时再自行实现。</item>
/// </list>
/// </remarks>
public sealed class LengthCalculatorOperator : IOperator
{
    private const string InputName = "source";
    private const string DefaultTargetField = "length_km";

    public OperatorMetadata Metadata { get; } = new()
    {
        Id = "length_calculator",
        Name = "线路长度计算器",
        Category = "插件扩展",
        Description = "计算线要素长度并写入指定字段（单位：公里；要求输入为米制坐标系，如 EPSG:3857）。",
        Tags = ["长度", "插件", "示例"],
        Version = "1.0.0",
        Parameters =
        [
            new ParameterDefinition
            {
                Name = "target_field",
                Type = "String",
                Required = false,
                Description = "写入长度值的目标字段名，默认 length_km"
            }
        ],
        InputSchema = new InputSchema
        {
            Description = "线要素集合（米制坐标系）"
        },
        OutputSchema = new OutputSchema
        {
            Description = "追加长度字段后的要素集合，其余属性原样保留"
        }
    };

    public ValidationResult Validate(AnalysisItem config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<ValidationError>();

        if (!config.Inputs.ContainsKey(InputName))
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgBindingIncomplete,
                Message = "缺少必需输入 'source'。"
            });
        }

        return new ValidationResult { Errors = errors, Warnings = [] };
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
        var logs = new List<ExecutionLogEntry>();

        try
        {
            if (!inputs.TryGetValue(InputName, out var source))
                return Fail("缺少输入 'source'", sw.Elapsed, logs);

            var targetField = ResolveTargetField(parameters);

            var results = new List<IFeature>();
            var count = 0L;

            await foreach (var feature in source.GetFeaturesAsync(cancellationToken: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (feature.Geometry is null)
                {
                    // 几何为空的要素无法计算长度，原样保留
                    results.Add(feature);
                    count++;
                    continue;
                }

                var lengthKm = feature.Geometry.Length / 1000.0;
                var attrs = new Dictionary<string, object?>(feature.Attributes)
                {
                    [targetField] = Math.Round(lengthKm, 3)
                };
                results.Add(new PluginFeature(feature.Id, feature.Geometry, attrs));
                count++;
            }

            context.Logger.LogInformation(
                "[LengthCalculator] 处理完成: {Count} 个要素，长度写入字段 '{Field}'",
                count, targetField);

            logs.Add(new ExecutionLogEntry
            {
                ExecutionId = context.ExecutionId,
                ItemId = context.CurrentItemId,
                OperatorId = Metadata.Id,
                Level = LogLevel.Information,
                Message = $"成功处理 {count} 个要素"
            });

            return new ExecutionResult
            {
                Status = ExecutionStatus.Success,
                Outputs = new Dictionary<string, object?>
                {
                    ["output"] = new PluginFeatureSource(results)
                },
                Elapsed = sw.Elapsed,
                Logs = logs
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
            context.Logger.LogError(ex, "[LengthCalculator] 执行失败");
            return Fail($"长度计算失败: {ex.Message}", sw.Elapsed, logs);
        }
    }

    private static string ResolveTargetField(IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters.TryGetValue("target_field", out var value) &&
            value is string name &&
            !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }
        return DefaultTargetField;
    }

    private static ExecutionResult Fail(string message, TimeSpan elapsed, List<ExecutionLogEntry> logs) =>
        new()
        {
            Status = ExecutionStatus.Failed,
            ErrorCode = ErrorCode.RtUnexpected,
            ErrorMessage = message,
            Elapsed = elapsed,
            Logs = logs
        };
}
