namespace OpenGisDAF.Core;

public sealed record OutputBinding
{
    public string SinkId { get; init; }
    public string? OutputKey { get; init; }
    public OutputSchema? Schema { get; init; }
}
