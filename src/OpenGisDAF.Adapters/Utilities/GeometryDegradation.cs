using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;

namespace OpenGisDAF.Adapters.Utilities;

public static class GeometryDegradation
{
    public static Geometry? Degrade(Geometry geometry, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        return geometry.OgcGeometryType switch
        {
            // Curve → LineString: extract control points and build a linestring
            OgcGeometryType.CircularString => DegradeCircularString((Geometry)geometry, logger),
            OgcGeometryType.CompoundCurve => DegradeCompoundCurve((Geometry)geometry, logger),
            OgcGeometryType.Curve => DegradeToLineString(geometry, logger),

            // Surface / CurvePolygon → Polygon: extract exterior ring
            OgcGeometryType.CurvePolygon => DegradeCurvePolygon((Geometry)geometry, logger),
            OgcGeometryType.Surface => DegradeSurface(geometry, logger),

            // TIN is not in NTS OgcGeometryType enum; handle via type check
            _ when geometry is NetTopologySuite.Geometries.GeometryCollection => geometry,

            // MultiSurface → MultiPolygon
            OgcGeometryType.MultiSurface => DegradeMultiSurface((Geometry)geometry, logger),

            _ => null // no degradation needed
        };
    }

    private static Geometry? DegradeCircularString(Geometry cs, ILogger? logger)
    {
        var coords = cs.Coordinates;
        if (coords.Length < 3) return null;

        logger?.LogWarning("Degrading CircularString to LineString: {CoordCount} coordinates, precision loss expected", coords.Length);

        // Interpolate circular arc to produce densified LineString
        var points = InterpolateArc(coords);
        return new LineString(points);
    }

    private static Geometry? DegradeCompoundCurve(Geometry cc, ILogger? logger)
    {
        logger?.LogWarning("Degrading CompoundCurve to LineString: precision loss expected");
        // Extract all coordinates and build a single LineString
        return new LineString(cc.Coordinates);
    }

    private static Geometry? DegradeToLineString(Geometry curve, ILogger? logger)
    {
        logger?.LogWarning("Degrading Curve to LineString: precision loss expected");
        return new LineString(curve.Coordinates);
    }

    private static Geometry? DegradeCurvePolygon(Geometry cp, ILogger? logger)
    {
        logger?.LogWarning("Degrading CurvePolygon to Polygon: extracting exterior boundary");
        var factory = cp.Factory;
        if (factory is null) return null;

        var shell = new LinearRing(cp.Coordinates);
        return factory.CreatePolygon(shell);
    }

    private static Geometry? DegradeSurface(Geometry surface, ILogger? logger)
    {
        logger?.LogWarning("Degrading Surface to Polygon: extracting exterior boundary");
        if (surface is Polygon polygon) return polygon; // already polygon

        var factory = surface.Factory;
        if (factory is null) return null;

        var shell = new LinearRing(surface.Coordinates);
        return factory.CreatePolygon(shell);
    }

    private static Geometry? DegradeMultiSurface(Geometry ms, ILogger? logger)
    {
        logger?.LogWarning("Degrading MultiSurface to MultiPolygon");
        if (ms is MultiPolygon mp) return mp;

        var factory = ms.Factory;
        if (factory is null) return null;

        var polygons = new List<Polygon>();
        for (int i = 0; i < ms.NumGeometries; i++)
        {
            var geom = ms.GetGeometryN(i);
            if (geom is Polygon p)
                polygons.Add(p);
            else
                polygons.Add(factory.CreatePolygon(new LinearRing(geom.Coordinates)));
        }
        return factory.CreateMultiPolygon(polygons.ToArray());
    }

    private static Coordinate[] InterpolateArc(Coordinate[] arcCoords)
    {
        // A circular arc is defined by 3 points: start, mid, end
        // Interpolate to produce a densified LineString approximation
        const int segmentsPerArc = 32;
        var result = new List<Coordinate>();

        for (int i = 0; i < arcCoords.Length - 2; i += 2)
        {
            var p0 = arcCoords[i];
            var p1 = arcCoords[i + 1];
            var p2 = arcCoords[i + 2];

            result.Add(p0);
            for (int j = 1; j < segmentsPerArc; j++)
            {
                double t = j / (double)segmentsPerArc;
                result.Add(InterpolateCircularPoint(p0, p1, p2, t));
            }
        }
        result.Add(arcCoords[^1]);

        return result.ToArray();
    }

    private static Coordinate InterpolateCircularPoint(Coordinate p0, Coordinate p1, Coordinate p2, double t)
    {
        // Use rational quadratic Bezier interpolation for circular arcs
        double w = Math.Cos(0.5 * AngleBetween(p0, p1, p2));

        double oneMinusT = 1.0 - t;
        double denom = oneMinusT * oneMinusT + 2.0 * w * oneMinusT * t + t * t;

        double x = (oneMinusT * oneMinusT * p0.X + 2.0 * w * oneMinusT * t * p1.X + t * t * p2.X) / denom;
        double y = (oneMinusT * oneMinusT * p0.Y + 2.0 * w * oneMinusT * t * p1.Y + t * t * p2.Y) / denom;

        return new Coordinate(x, y);
    }

    private static double AngleBetween(Coordinate p0, Coordinate p1, Coordinate p2)
    {
        double dx1 = p0.X - p1.X, dy1 = p0.Y - p1.Y;
        double dx2 = p2.X - p1.X, dy2 = p2.Y - p1.Y;
        double dot = dx1 * dx2 + dy1 * dy2;
        double norm1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
        double norm2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);
        return Math.Acos(Math.Clamp(dot / (norm1 * norm2), -1.0, 1.0));
    }
}
