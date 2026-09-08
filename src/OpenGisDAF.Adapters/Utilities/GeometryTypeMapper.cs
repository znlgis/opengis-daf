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

    public static Core.GeometryType? FromOguGeometryType(OguGeometryType type) => type switch
    {
        OguGeometryType.POINT => Core.GeometryType.Point,
        OguGeometryType.MULTIPOINT => Core.GeometryType.MultiPoint,
        OguGeometryType.LINESTRING => Core.GeometryType.LineString,
        OguGeometryType.MULTILINESTRING => Core.GeometryType.MultiLineString,
        OguGeometryType.POLYGON => Core.GeometryType.Polygon,
        OguGeometryType.MULTIPOLYGON => Core.GeometryType.MultiPolygon,
        OguGeometryType.GEOMETRYCOLLECTION => Core.GeometryType.GeometryCollection,
        _ => null
    };
}
