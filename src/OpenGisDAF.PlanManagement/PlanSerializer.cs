using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenGisDAF.Core;
using OpenGisDAF.Infrastructure;

namespace OpenGisDAF.PlanManagement;

public sealed class PlanSerializer : IPlanSerializer
{
    private readonly JsonSerializerOptions _options;

    public PlanSerializer()
    {
        _options = JsonConfiguration.Create(options =>
        {
            options.Converters.Add(new TimeSpanConverter());
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.MaxDepth = 32;
        });
    }

    public string Serialize(AnalysisPlan plan)
    {
        return JsonSerializer.Serialize(plan, _options);
    }

    public AnalysisPlan Deserialize(string json)
    {
        return JsonSerializer.Deserialize<AnalysisPlan>(json, _options)
               ?? throw new JsonException("Failed to deserialize AnalysisPlan.");
    }

    public async Task<string> SerializeAsync(AnalysisPlan plan, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, plan, _options, cancellationToken);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public async Task<AnalysisPlan> DeserializeAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var result = await JsonSerializer.DeserializeAsync<AnalysisPlan>(stream, _options, cancellationToken);
        return result ?? throw new JsonException("Failed to deserialize AnalysisPlan.");
    }
}
