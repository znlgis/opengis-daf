namespace OpenGisDAF.Core;

public sealed record ResourceUsage
{
    public long PeakMemoryBytes { get; init; }
    public double? AvgCpuPercent { get; init; }
    public long DataReadBytes { get; init; }
}
