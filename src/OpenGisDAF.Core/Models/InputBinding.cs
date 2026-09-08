namespace OpenGisDAF.Core;

public sealed record InputBinding
{
    public BindingType Type { get; init; }
    public string SourceId { get; init; } = null!;
    public string? OutputKey { get; init; }
    public ConnectionConfig? ConnectionConfig { get; init; }
}
