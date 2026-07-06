using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using OpenGisDAF.Adapters;
using OpenGisDAF.Core;
using ExecutionContext = OpenGisDAF.Core.ExecutionContext;

namespace OpenGisDAF.Operators;

public sealed class NullValueFiller : IOperator
{
    public OperatorMetadata Metadata { get; } = new()
    {
        Id = "null_value_filler",
        Name = "空值填充器",
        Category = "属性操作",
        Description = "将指定字段的 null 值或空字符串替换为指定的默认值。",
        Tags = ["空值", "填充", "属性", "默认值"],
        Version = "1.0.0",
        Parameters =
        [
            new ParameterDefinition
            {
                Name = "target_field",
                Type = "String",
                Required = true,
                Description = "需要填充空值的目标字段名"
            },
            new ParameterDefinition
            {
                Name = "default_value",
                Type = "String",
                Required = true,
                Description = "当目标字段为空时使用的默认值"
            },
            new ParameterDefinition
            {
                Name = "field_type",
                Type = "String",
                Required = true,
                Description = "目标字段的字段类型",
                Constraint = new ParameterConstraint
                {
                    AllowedValues = ["String", "Integer", "Double", "Boolean", "DateTime"]
                }
            }
        ],
        InputSchema = new InputSchema
        {
            Description = "源要素集，支持任意几何类型。"
        },
        OutputSchema = new OutputSchema
        {
            ProducedFields = [],
            Description = "输出要素集，空值字段已填充默认值。"
        }
    };

    public ValidationResult Validate(AnalysisItem config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationError>();

        if (!config.Parameters.TryGetValue("target_field", out var tf) ||
            tf is not string targetField || string.IsNullOrWhiteSpace(targetField))
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgParamOutOfRange,
                Message = "参数 'target_field' 是必需的，且不能为空。"
            });
        }

        if (!config.Parameters.TryGetValue("default_value", out var dv) ||
            dv is not string defaultVal || string.IsNullOrWhiteSpace(defaultVal))
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgParamOutOfRange,
                Message = "参数 'default_value' 是必需的，且不能为空。"
            });
        }

        var fieldTypeStr = OperatorHelper.GetStringParam(config.Parameters, "field_type");
        if (fieldTypeStr is null)
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgParamOutOfRange,
                Message = "参数 'field_type' 是必需的。"
            });
        }
        else if (OperatorHelper.ParseFieldType(fieldTypeStr) is null)
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgParamOutOfRange,
                Message = $"无效的 field_type: '{fieldTypeStr}'，支持: String, Integer, Double, Boolean, DateTime。"
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
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(context);

        var sw = Stopwatch.StartNew();
        var logs = new List<ExecutionLogEntry>();

        try
        {
            var targetField = (string)parameters["target_field"]!;
            var defaultStr = (string)parameters["default_value"]!;
            var fieldType = OperatorHelper.ParseFieldType((string)parameters["field_type"]!)!.Value;
            var defaultValue = OperatorHelper.CoerceTo(defaultStr, fieldType);

            if (!inputs.TryGetValue("source", out var source))
            {
                return OperatorHelper.Fail("缺少输入 'source'", sw.Elapsed, logs);
            }

            var results = new List<IFeature>();
            var total = 0;
            var filled = 0;

            await foreach (var feature in source.GetFeaturesAsync(cancellationToken: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                total++;

                var currentValue = feature.Attributes.TryGetValue(targetField, out var v) ? v : null;
                var isNull = currentValue is null || (currentValue is string s && s.Length == 0);

                if (isNull)
                {
                    filled++;
                    var attrs = new Dictionary<string, object?>(feature.Attributes)
                    {
                        [targetField] = defaultValue
                    };
                    results.Add(new SimpleFeature(feature.Id, feature.Geometry, attrs));
                }
                else
                {
                    results.Add(feature);
                }
            }

            context.Logger.LogInformation(
                "[NullValueFiller] 处理完成: {Total} 个要素，填充 {Filled} 个空值，字段 '{Field}'",
                total, filled, targetField);

            logs.Add(new ExecutionLogEntry
            {
                ExecutionId = context.ExecutionId,
                ItemId = string.Empty,
                OperatorId = Metadata.Id,
                Level = LogLevel.Information,
                Message = $"处理 {total} 个要素，填充 {filled} 个空值"
            });

            return new ExecutionResult
            {
                Status = ExecutionStatus.Success,
                Outputs = new Dictionary<string, object?>
                {
                    ["output"] = new InMemoryFeatureSource(results)
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
            context.Logger.LogError(ex, "[NullValueFiller] 执行失败");
            return OperatorHelper.Fail($"空值填充失败: {ex.Message}", sw.Elapsed, logs);
        }
    }
}
