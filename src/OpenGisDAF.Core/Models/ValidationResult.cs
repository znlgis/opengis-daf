namespace OpenGisDAF.Core;

public sealed record ValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public IReadOnlyList<ValidationError> Errors { get; init; } = [];
    public IReadOnlyList<ValidationError> Warnings { get; init; } = [];
}
