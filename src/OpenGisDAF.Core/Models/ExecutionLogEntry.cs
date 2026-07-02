using Microsoft.Extensions.Logging;

namespace OpenGisDAF.Core;

public sealed record ExecutionLogEntry
{
    public string ExecutionId { get; init; }
    public string ItemId { get; init; }
    public string OperatorId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public LogLevel Level { get; init; }
    public string Message { get; init; }
    public string? FeatureId { get; init; }
}
