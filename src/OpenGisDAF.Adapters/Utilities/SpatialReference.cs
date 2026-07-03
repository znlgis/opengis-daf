using OpenGisDAF.Core;

namespace OpenGisDAF.Adapters.Utilities;

public sealed class SpatialReference : ISpatialReference
{
    public string Authority { get; }
    public int Code { get; }
    public string Wkt { get; }

    public SpatialReference(int epsgCode, string wkt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(epsgCode, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(wkt);

        Authority = "EPSG";
        Code = epsgCode;
        Wkt = wkt;
    }

    public bool IsEquivalentTo(ISpatialReference other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Authority == other.Authority && Code == other.Code;
    }

    public override string ToString() => $"{Authority}:{Code}";
}
