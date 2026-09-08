using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenGisDAF.Infrastructure.JsonConverters;

public sealed class AnalysisParametersJsonConverter : JsonConverter<IReadOnlyDictionary<string, object?>>
{
    public override IReadOnlyDictionary<string, object?> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Analysis parameters must be a JSON object.");

        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => Normalize(property.Value), StringComparer.Ordinal);
    }

    public override void Write(
        Utf8JsonWriter writer,
        IReadOnlyDictionary<string, object?> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (name, parameter) in value)
        {
            writer.WritePropertyName(name);
            JsonSerializer.Serialize(writer, parameter, parameter?.GetType() ?? typeof(object), options);
        }

        writer.WriteEndObject();
    }

    private static object? Normalize(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => value.GetDouble(),
        _ => value.Clone()
    };
}
