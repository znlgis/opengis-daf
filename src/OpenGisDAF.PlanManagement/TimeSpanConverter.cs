namespace OpenGisDAF.PlanManagement;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class TimeSpanConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        if (string.IsNullOrWhiteSpace(str))
            return TimeSpan.Zero;

        if (TimeSpan.TryParse(str, CultureInfo.InvariantCulture, out var result))
            return result;

        throw new JsonException($"无效的 TimeSpan 格式: '{str}'（期望如 '00:00:30' 或 '1.02:03:04'）");
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("c", CultureInfo.InvariantCulture));
    }
}
