namespace OpenGisDAF.Core;

public sealed record ExecutionResult
{
    public ExecutionStatus Status { get; init; }
    public IReadOnlyDictionary<string, object?> Outputs { get; init; }
    public IReadOnlyList<ExecutionLogEntry> Logs { get; init; }
    public TimeSpan Elapsed { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
