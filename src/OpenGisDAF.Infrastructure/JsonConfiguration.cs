using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenGisDAF.Infrastructure;

public static class JsonConfiguration
{
    public static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    public static JsonSerializerOptions Create(Action<JsonSerializerOptions>? configure = null)
    {
        var options = new JsonSerializerOptions(DefaultOptions);
        configure?.Invoke(options);
        return options;
    }
}
