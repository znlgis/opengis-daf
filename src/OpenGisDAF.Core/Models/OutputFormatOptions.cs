namespace OpenGisDAF.Core;

public sealed class OutputFormatOptions
{
    public int? DecimalPlaces { get; init; }
    public string? DateFormat { get; init; }
    public string? Encoding { get; init; }
    public bool WriteHeader { get; init; } = true;
}
