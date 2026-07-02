namespace OpenGisDAF.Core;

public sealed class ConnectionConfig
{
    public string DataSourceId { get; init; }
    public string AdapterType { get; init; }
    public string Host { get; init; }
    public int Port { get; init; }
    public string Database { get; init; }
    public string UserName { get; init; }
    public string? EncryptedPassword { get; init; }
    public IReadOnlyDictionary<string, string> AdditionalOptions { get; init; }
}
