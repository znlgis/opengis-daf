namespace OpenGisDAF.Core;

public sealed class GlobalConcurrencyPolicy
{
    public int MaxConcurrentPlans { get; init; } = 4;
    public int MaxConcurrentItems { get; init; } = 16;
}
