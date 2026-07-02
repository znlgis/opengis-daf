namespace OpenGisDAF.Core;

public sealed record ExecutionLogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public string Level { get; init; }
    public string Message { get; init; }
    public string? ItemId { get; init; }
    public IReadOnlyDictionary<string, object?>? Context { get; init; }
}
