using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenGisDAF.Infrastructure;

public static class JsonConfiguration
{
    public static readonly JsonSerializerOptions DefaultOptions;

    static JsonConfiguration()
    {
        DefaultOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };
        DefaultOptions.MakeReadOnly();
    }

    /// <summary>
    /// Creates a customized copy of DefaultOptions. The returned instance
    /// should be cached and reused — creating new JsonSerializerOptions
    /// frequently degrades performance due to metadata rebuilds.
    /// Prefer DefaultOptions directly when no customization is needed.
    /// </summary>
    public static JsonSerializerOptions Create(Action<JsonSerializerOptions>? configure = null)
    {
        var options = new JsonSerializerOptions(DefaultOptions);
        configure?.Invoke(options);
        return options;
    }
}
