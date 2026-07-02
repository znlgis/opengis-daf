namespace OpenGisDAF.Core;

public sealed record ValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<ValidationError> Errors { get; init; } = [];
    public IReadOnlyList<ValidationError> Warnings { get; init; } = [];
}
