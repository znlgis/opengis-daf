using System.Globalization;
using OpenGisDAF.Core;

namespace OpenGisDAF.Operators;

internal static class OperatorHelper
{
    public static FieldType? ParseFieldType(string type) => type.ToLowerInvariant() switch
    {
        "string" => FieldType.String,
        "integer" => FieldType.Integer,
        "double" => FieldType.Double,
        "boolean" => FieldType.Boolean,
        "datetime" => FieldType.DateTime,
        _ => null
    };

    public static object? CoerceTo(object? value, FieldType targetType)
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

    public static string? GetStringParam(IReadOnlyDictionary<string, object?> parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var val)) return null;
        return val is string s && !string.IsNullOrWhiteSpace(s) ? s : null;
    }

    public static ExecutionResult Fail(string message, TimeSpan elapsed, IReadOnlyList<ExecutionLogEntry>? logs = null)
    {
        return new ExecutionResult
        {
            Status = ExecutionStatus.Failed,
            ErrorCode = ErrorCode.RtUnexpected,
            ErrorMessage = message,
            Elapsed = elapsed,
            Logs = logs ?? []
        };
    }
}
