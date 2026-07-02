namespace OpenGisDAF.Core;

public sealed record ValidationError
{
    public ValidationSeverity Severity { get; init; }
    public string Code { get; init; } = null!;
    public string Message { get; init; } = null!;
    public string? Location { get; init; }
}
