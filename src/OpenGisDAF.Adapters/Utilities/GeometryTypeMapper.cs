using OguGeometryType = OpenGIS.Utils.Engine.Enums.GeometryType;

namespace OpenGisDAF.Adapters.Utilities;

public static class GeometryTypeMapper
{
    public static OguGeometryType ToOguGeometryType(Core.GeometryType? type) => type switch
    {
        Core.GeometryType.Point => OguGeometryType.POINT,
        Core.GeometryType.MultiPoint => OguGeometryType.MULTIPOINT,
        Core.GeometryType.LineString => OguGeometryType.LINESTRING,
        Core.GeometryType.MultiLineString => OguGeometryType.MULTILINESTRING,
        Core.GeometryType.Polygon => OguGeometryType.POLYGON,
        Core.GeometryType.MultiPolygon => OguGeometryType.MULTIPOLYGON,
        Core.GeometryType.GeometryCollection => OguGeometryType.GEOMETRYCOLLECTION,
        _ => OguGeometryType.UNKNOWN
    };
}
