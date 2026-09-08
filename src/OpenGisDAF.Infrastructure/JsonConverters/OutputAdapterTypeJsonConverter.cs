using System.Text.Json;
using System.Text.Json.Serialization;
using OpenGisDAF.Core;

namespace OpenGisDAF.Infrastructure.JsonConverters;

public sealed class OutputAdapterTypeJsonConverter : JsonConverter<OutputAdapterType>
{
    public override OutputAdapterType Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new JsonException("Output adapter type must be a non-empty string.");

        return value.ToLowerInvariant() switch
        {
            "console" or "consolewriter" => OutputAdapterType.ConsoleWriter,
            "geojson" or "geojsonwriter" => OutputAdapterType.GeoJsonWriter,
            "shapefile" or "shapefilewriter" => OutputAdapterType.ShapefileWriter,
            "postgis" or "postgiswriter" => OutputAdapterType.PostGISWriter,
            _ => throw new JsonException($"Unknown output adapter type '{value}'.")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        OutputAdapterType value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
    }
}
