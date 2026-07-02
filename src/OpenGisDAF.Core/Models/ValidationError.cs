namespace OpenGisDAF.Core;

public sealed record ValidationError
{
    public ValidationSeverity Severity { get; init; }
    public string Code { get; init; }
    public string Message { get; init; }
    public string? Location { get; init; }
}
