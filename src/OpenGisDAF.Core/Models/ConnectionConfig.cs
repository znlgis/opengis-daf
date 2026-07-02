namespace OpenGisDAF.Core;

public sealed class ConnectionConfig
{
    public string DataSourceId { get; init; } = null!;
    public string AdapterType { get; init; } = null!;
    public string Host { get; init; } = null!;
    public int Port { get; init; }
    public string Database { get; init; } = null!;
    public string UserName { get; init; } = null!;
    public string? EncryptedPassword { get; init; }
    public IReadOnlyDictionary<string, string> AdditionalOptions { get; init; } = new Dictionary<string, string>();
}
