using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using OpenGisDAF.Adapters;
using OpenGisDAF.Core;
using ExecutionContext = OpenGisDAF.Core.ExecutionContext;

namespace OpenGisDAF.Operators;

public sealed class FieldCalculator : IOperator
{
    private static readonly Regex FieldRefRegex = new(@"\{(\w+)\}", RegexOptions.Compiled);

    public OperatorMetadata Metadata { get; } = new()
    {
        Id = "field_calculator",
        Name = "字段计算器",
        Category = "属性操作",
        Description = "根据表达式计算字段值，支持字面量、{字段名} 引用和 + - * / 运算。",
        Tags = ["字段计算", "属性", "表达式"],
        Version = "1.0.0",
        Parameters =
        [
            new ParameterDefinition
            {
                Name = "target_field",
                Type = "String",
                Required = true,
                Description = "目标字段名（新的或已存在的字段）"
            },
            new ParameterDefinition
            {
                Name = "expression",
                Type = "String",
                Required = true,
                Description = "计算表达式，支持 \"字面量\"、{字段名} 引用及 + - * / 运算"
            },
            new ParameterDefinition
            {
                Name = "field_type",
                Type = "String",
                Required = true,
                Description = "目标字段类型",
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
            Description = "输出要素集，保留原有全部属性并追加/覆盖计算字段。"
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

        if (!config.Parameters.TryGetValue("expression", out var exp) ||
            exp is not string expression || string.IsNullOrWhiteSpace(expression))
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgParamOutOfRange,
                Message = "参数 'expression' 是必需的，且不能为空。"
            });
        }

        var fieldTypeStr = GetStringParam(config.Parameters, "field_type");
        if (fieldTypeStr is null)
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgParamOutOfRange,
                Message = "参数 'field_type' 是必需的。"
            });
        }
        else if (ParseFieldType(fieldTypeStr) is null)
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

        var sw = Stopwatch.StartNew();
        var logs = new List<ExecutionLogEntry>();

        try
        {
            var targetField = (string)parameters["target_field"]!;
            var expression = (string)parameters["expression"]!;
            var fieldType = ParseFieldType((string)parameters["field_type"]!)!.Value;

            if (!inputs.TryGetValue("source", out var source))
            {
                return Fail("缺少输入 'source'", sw.Elapsed, logs);
            }

            var fieldRefs = ExtractFieldRefs(expression);
            var results = new List<IFeature>();
            var count = 0;

            await foreach (var feature in source.GetFeaturesAsync(cancellationToken: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var computed = ComputeExpression(expression, feature.Attributes, fieldRefs, fieldType);
                var attrs = new Dictionary<string, object?>(feature.Attributes) { [targetField] = computed };
                results.Add(new Feature(feature.Id, feature.Geometry, attrs));
                count++;
            }

            context.Logger.LogInformation(
                "[FieldCalculator] 处理完成: {Count} 个要素，目标字段 '{Field}'，表达式 '{Expr}'",
                count, targetField, expression);

            logs.Add(new ExecutionLogEntry
            {
                ExecutionId = context.ExecutionId,
                ItemId = string.Empty,
                OperatorId = Metadata.Id,
                Level = LogLevel.Information,
                Message = $"成功处理 {count} 个要素"
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
            context.Logger.LogError(ex, "[FieldCalculator] 执行失败");
            return Fail($"字段计算失败: {ex.Message}", sw.Elapsed, logs);
        }
    }

    private static HashSet<string> ExtractFieldRefs(string expression)
    {
        var refs = new HashSet<string>();
        foreach (Match m in FieldRefRegex.Matches(expression))
            refs.Add(m.Groups[1].Value);
        return refs;
    }

    private object? ComputeExpression(
        string expression,
        IReadOnlyDictionary<string, object?> attrs,
        HashSet<string> fieldRefs,
        FieldType targetType)
    {
        // String literal: "hello"
        if (expression.Length >= 2 && expression[0] == '"' && expression[^1] == '"')
            return SafeCoerceTo(expression[1..^1], targetType);

        var trimmed = expression.Trim();

        // Pure field reference: {field}
        if (trimmed.Length >= 3 && trimmed[0] == '{' && trimmed[^1] == '}' && fieldRefs.Count == 1)
        {
            var refName = fieldRefs.First();
            var rawValue = attrs.TryGetValue(refName, out var v) ? v : null;
            return SafeCoerceTo(rawValue, targetType);
        }

        // Substitute field references
        var substituted = expression;
        foreach (var refName in fieldRefs)
        {
            var raw = attrs.TryGetValue(refName, out var val) ? val : null;
            substituted = substituted.Replace($"{{{refName}}}", FormatValue(raw));
        }

        // Try numeric literal after substitution
        if (double.TryParse(substituted, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            return SafeCoerceTo(num, targetType);

        // Arithmetic expression
        var evalResult = EvaluateArithmetic(substituted);
        return SafeCoerceTo(evalResult, targetType);
    }

    private static object? SafeCoerceTo(object? value, FieldType targetType)
    {
        try
        {
            return CoerceTo(value, targetType);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string FormatValue(object? value)
    {
        if (value is null) return "0";
        if (value is double d) return d.ToString("G", CultureInfo.InvariantCulture);
        if (value is float f) return f.ToString("G", CultureInfo.InvariantCulture);
        if (value is decimal m) return m.ToString("G", CultureInfo.InvariantCulture);
        if (value is int i) return i.ToString(CultureInfo.InvariantCulture);
        if (value is long l) return l.ToString(CultureInfo.InvariantCulture);
        return value.ToString() ?? "0";
    }

    private static object? CoerceTo(object? value, FieldType targetType)
    {
        if (value is null) return null;

        return targetType switch
        {
            FieldType.String => value.ToString()!,
            FieldType.Integer => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            FieldType.Double => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            FieldType.Boolean => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
            FieldType.DateTime => Convert.ToDateTime(value, CultureInfo.InvariantCulture),
            _ => value.ToString()!
        };
    }

    private static double EvaluateArithmetic(string expr)
    {
        var parser = new MathParser(expr);
        var result = parser.ParseExpression();
        if (parser.HasMore)
            throw new FormatException($"表达式在位置 {parser.Pos} 处有未预期的字符");
        return result;
    }

    private static FieldType? ParseFieldType(string type) => type.ToLowerInvariant() switch
    {
        "string" => FieldType.String,
        "integer" => FieldType.Integer,
        "double" => FieldType.Double,
        "boolean" => FieldType.Boolean,
        "datetime" => FieldType.DateTime,
        _ => null
    };

    private static string? GetStringParam(IReadOnlyDictionary<string, object?> parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var val)) return null;
        return val is string s && !string.IsNullOrWhiteSpace(s) ? s : null;
    }

    private static ExecutionResult Fail(string message, TimeSpan elapsed, IReadOnlyList<ExecutionLogEntry> logs)
    {
        return new ExecutionResult
        {
            Status = ExecutionStatus.Failed,
            ErrorCode = ErrorCode.RtUnexpected,
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

    /// <summary>
    /// 简化的递归下降算术解析器，支持 + - * / 和括号。
    /// </summary>
    private sealed class MathParser
    {
        private readonly string _expr;
        private int _pos;

        public MathParser(string expr) { _expr = expr; _pos = 0; }
        public int Pos => _pos;
        public bool HasMore => SkipWhitespace() && _pos < _expr.Length;

        public double ParseExpression()
        {
            var left = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (_pos >= _expr.Length) break;
                var op = _expr[_pos];
                if (op != '+' && op != '-') break;
                _pos++;
                var right = ParseTerm();
                left = op == '+' ? left + right : left - right;
            }
            return left;
        }

        private double ParseTerm()
        {
            var left = ParseFactor();
            while (true)
            {
                SkipWhitespace();
                if (_pos >= _expr.Length) break;
                var op = _expr[_pos];
                if (op != '*' && op != '/') break;
                _pos++;
                var right = ParseFactor();
                if (op == '/' && Math.Abs(right) < 1e-15)
                    throw new DivideByZeroException("除以零");
                left = op == '*' ? left * right : left / right;
            }
            return left;
        }

        private double ParseFactor()
        {
            SkipWhitespace();
            if (_pos >= _expr.Length)
                throw new FormatException("表达式意外结束");

            if (_expr[_pos] == '(')
            {
                _pos++;
                var result = ParseExpression();
                SkipWhitespace();
                if (_pos >= _expr.Length || _expr[_pos] != ')')
                    throw new FormatException("缺少右括号");
                _pos++;
                return result;
            }

            if (_expr[_pos] == '-')
            {
                _pos++;
                return -ParseFactor();
            }

            return ParseNumber();
        }

        private double ParseNumber()
        {
            SkipWhitespace();
            var start = _pos;
            while (_pos < _expr.Length && (char.IsDigit(_expr[_pos]) || _expr[_pos] == '.'))
                _pos++;
            if (_pos == start)
                throw new FormatException($"位置 {_pos} 处期望数字");

            if (!double.TryParse(_expr[start.._pos], NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                throw new OverflowException($"位置 {start} 处的数值超出可表示范围");

            return num;
        }

        private bool SkipWhitespace()
        {
            while (_pos < _expr.Length && char.IsWhiteSpace(_expr[_pos]))
                _pos++;
            return true;
        }
    }
}
