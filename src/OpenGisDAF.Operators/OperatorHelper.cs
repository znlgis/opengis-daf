using System.Globalization;
using System.Text.Json;
using OpenGisDAF.Core;

namespace OpenGisDAF.Operators;

internal static class OperatorHelper
{
    public static FeatureSchemaMetadata InheritSchema(IFeatureSource source, GeometryType? geometryType = null, ISpatialReference? spatialReference = null, FieldDefinition? addedField = null)
    {
        var inherited = source is IFeatureSchemaProvider provider
            ? provider.Schema
            : new FeatureSchemaMetadata
            {
                GeometryType = geometryType,
                SpatialReference = source.SpatialReference
            };
        var fields = inherited.Fields;
        if (addedField is not null && !fields.Any(field => string.Equals(field.Name, addedField.Name, StringComparison.OrdinalIgnoreCase)))
            fields = fields.Append(addedField).ToArray();
        return inherited with
        {
            Fields = fields,
            GeometryType = geometryType ?? inherited.GeometryType,
            SpatialReference = spatialReference ?? inherited.SpatialReference
        };
    }

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
        var value = val switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null
        };
        return !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    public static int? GetIntParam(IReadOnlyDictionary<string, object?> parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var val)) return null;
        if (val is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var jsonInt)) return jsonInt;
            if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var jsonStringInt)) return jsonStringInt;
            return null;
        }

        return val switch
        {
            int i => i,
            long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
            double d when d is >= int.MinValue and <= int.MaxValue && d % 1 == 0 => (int)d,
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
            _ => null
        };
    }

    public static double? GetDoubleParam(IReadOnlyDictionary<string, object?> parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var val)) return null;
        if (val is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var jsonDouble)) return jsonDouble;
            if (element.ValueKind == JsonValueKind.String && double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var jsonStringDouble)) return jsonStringDouble;
            return null;
        }

        return val switch
        {
            double d => d,
            float f => f,
            decimal m => (double)m,
            int i => i,
            long l => l,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null
        };
    }

    public static bool? GetBoolParam(IReadOnlyDictionary<string, object?> parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var val)) return null;
        if (val is JsonElement element)
        {
            if (element.ValueKind is JsonValueKind.True or JsonValueKind.False) return element.GetBoolean();
            if (element.ValueKind == JsonValueKind.String && bool.TryParse(element.GetString(), out var jsonBool)) return jsonBool;
            return null;
        }

        return val switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var b) => b,
            _ => null
        };
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
