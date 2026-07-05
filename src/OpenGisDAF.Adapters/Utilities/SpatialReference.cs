using OpenGisDAF.Core;

namespace OpenGisDAF.Adapters.Utilities;

public sealed class SpatialReference : ISpatialReference
{
    public string Authority { get; }
    public int Code { get; }
    public string Wkt { get; }

    public SpatialReference(int epsgCode, string wkt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(epsgCode, 0);
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

    public static string BuildWkt(int epsgCode)
    {
        if (epsgCode == 4326)
            return """GEOGCS["WGS 84",DATUM["WGS_1984",SPHEROID["WGS 84",6378137,298.257223563,AUTHORITY["EPSG","7030"]],AUTHORITY["EPSG","6326"]],PRIMEM["Greenwich",0,AUTHORITY["EPSG","8901"]],UNIT["degree",0.0174532925199433,AUTHORITY["EPSG","9122"]],AUTHORITY["EPSG","4326"]]""";

        if (epsgCode == 3857)
            return """PROJCS["WGS 84 / Pseudo-Mercator",GEOGCS["WGS 84",DATUM["WGS_1984",SPHEROID["WGS 84",6378137,298.257223563,AUTHORITY["EPSG","7030"]],AUTHORITY["EPSG","6326"]],PRIMEM["Greenwich",0,AUTHORITY["EPSG","8901"]],UNIT["degree",0.0174532925199433,AUTHORITY["EPSG","9122"]],AUTHORITY["EPSG","4326"]],PROJECTION["Mercator_1SP"],PARAMETER["central_meridian",0],PARAMETER["scale_factor",1],PARAMETER["false_easting",0],PARAMETER["false_northing",0],UNIT["metre",1,AUTHORITY["EPSG","9001"]],AXIS["Easting",EAST],AXIS["Northing",NORTH],AUTHORITY["EPSG","3857"]]""";

        if (epsgCode == 4490)
            return """GEOGCS["China Geodetic Coordinate System 2000",DATUM["China_2000",SPHEROID["CGCS2000",6378137,298.257222101,AUTHORITY["EPSG","1024"]],AUTHORITY["EPSG","1043"]],PRIMEM["Greenwich",0,AUTHORITY["EPSG","8901"]],UNIT["degree",0.0174532925199433,AUTHORITY["EPSG","9122"]],AUTHORITY["EPSG","4490"]]""";

        return $"""GEOGCS["EPSG:{epsgCode}",DATUM["Unknown",SPHEROID["Unknown",0,0]],PRIMEM["Greenwich",0],UNIT["degree",0.0174532925199433],AUTHORITY["EPSG","{epsgCode}"]]""";
    }

    public override string ToString() => $"{Authority}:{Code}";
}
