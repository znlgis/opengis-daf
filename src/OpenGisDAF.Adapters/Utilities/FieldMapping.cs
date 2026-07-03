namespace OpenGisDAF.Adapters.Utilities;

public sealed record FieldMapping
{
    public string SourceField { get; init; } = null!;
    public string TargetField { get; init; } = null!;
    public Func<object?, object?>? Converter { get; init; }

    public object? Convert(object? sourceValue) =>
        Converter is not null ? Converter(sourceValue) : sourceValue;
}
