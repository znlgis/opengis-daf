namespace OpenGisDAF.Core;

public sealed class OutputBinding
{
    public string AdapterType { get; init; } = null!;
    public string TargetPath { get; init; } = null!;
    public ConnectionConfig? ConnectionConfig { get; init; }
    public IReadOnlyList<string>? FieldSelection { get; init; }
    public bool IsIntermediate { get; init; } = false;
    public OutputFormatOptions? FormatOptions { get; init; }
}
