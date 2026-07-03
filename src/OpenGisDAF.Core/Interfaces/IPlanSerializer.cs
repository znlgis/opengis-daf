namespace OpenGisDAF.Core;

public interface IPlanSerializer
{
    string Serialize(AnalysisPlan plan);
    AnalysisPlan Deserialize(string json);
    Task<string> SerializeAsync(AnalysisPlan plan, CancellationToken cancellationToken = default);
    Task<AnalysisPlan> DeserializeAsync(Stream stream, CancellationToken cancellationToken = default);
}
