namespace OpenGisDAF.Core;

public sealed class OutputBinding
{
    public OutputBinding(OutputAdapterType adapterType, string targetPath)
    {
        AdapterType = adapterType;
        TargetPath = targetPath;
    }

    public OutputAdapterType AdapterType { get; init; }
    public string TargetPath { get; init; } = string.Empty;
    public ConnectionConfig? ConnectionConfig { get; init; }
    public IReadOnlyList<string>? FieldSelection { get; init; }
    public bool IsIntermediate { get; init; } = false;
    public OutputFormatOptions? FormatOptions { get; init; }

    public OutputBinding() { } // for deserialization
}
