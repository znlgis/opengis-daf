using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace OpenGisDAF.Adapters.Utilities;

public static class WktConverter
{
    private static readonly ThreadLocal<WKTReader> Reader = new(() => new WKTReader());
    private static readonly WKTWriter Writer = new();

    public static string ToWkt(Geometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return Writer.Write(geometry);
    }

    public static Geometry FromWkt(string wkt)
    {
        ArgumentNullException.ThrowIfNull(wkt);
        return Reader.Value!.Read(wkt);
    }

    public static bool TryParse(string wkt, out Geometry? geometry)
    {
        geometry = null;
        if (string.IsNullOrWhiteSpace(wkt)) return false;

        try
        {
            geometry = Reader.Value!.Read(wkt);
            return true;
        }
        catch (ParseException)
        {
            return false;
        }
    }
}
