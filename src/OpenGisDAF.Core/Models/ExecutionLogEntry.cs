using Microsoft.Extensions.Logging;

namespace OpenGisDAF.Core;

public sealed record ExecutionLogEntry
{
    public string ExecutionId { get; init; } = null!;
    public string ItemId { get; init; } = null!;
    public string OperatorId { get; init; } = null!;
    public DateTimeOffset Timestamp { get; init; }
    public LogLevel Level { get; init; }
    public string Message { get; init; } = null!;
    public string? FeatureId { get; init; }
}
