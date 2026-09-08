using System.Text.Json;
using System.Text.Json.Serialization;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace OpenGisDAF.Core.JsonConverters;

public sealed class GeometryWktJsonConverter : JsonConverter<Geometry>
{
    private static readonly ThreadLocal<WKTReader> Reader = new(() => new WKTReader());
    private static readonly WKTWriter Writer = new() { OutputOrdinates = Ordinates.XYZ };

    public override Geometry? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        var wkt = reader.GetString();
        return wkt is null ? null : Reader.Value!.Read(wkt);
    }

    public override void Write(Utf8JsonWriter writer, Geometry value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStringValue(Writer.Write(value));
    }
}
