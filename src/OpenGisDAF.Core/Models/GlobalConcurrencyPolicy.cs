namespace OpenGisDAF.Core;

public sealed class GlobalConcurrencyPolicy
{
    public int MaxGlobalParallelism { get; init; } = 16;
    public bool Enabled { get; init; } = true;
}
