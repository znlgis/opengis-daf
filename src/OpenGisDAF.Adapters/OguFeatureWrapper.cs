using NetTopologySuite.Geometries;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGisDAF.Adapters.Utilities;
using OpenGisDAF.Core;

namespace OpenGisDAF.Adapters;

internal sealed class OguFeatureWrapper : IFeature
{
    private readonly OguFeature _feature;
    private Geometry? _cachedGeometry;
    private IReadOnlyDictionary<string, object?>? _cachedAttributes;

    public OguFeatureWrapper(OguFeature feature)
    {
        _feature = feature ?? throw new ArgumentNullException(nameof(feature));
    }

    public string Id => _feature.Fid.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public Geometry Geometry
    {
        get
        {
            if (_cachedGeometry is not null) return _cachedGeometry;
            if (_feature.Wkt is null)
            {
                _cachedGeometry = GeometryFactory.Default.CreatePoint(new Coordinate(0, 0));
                return _cachedGeometry;
            }

            _cachedGeometry = WktConverter.FromWkt(_feature.Wkt);
            return _cachedGeometry;
        }
    }

    public IReadOnlyDictionary<string, object?> Attributes
    {
        get
        {
            if (_cachedAttributes is not null) return _cachedAttributes;

            _cachedAttributes = _feature.Attributes.ToDictionary(
                kvp => kvp.Key,
                kvp => ExtractValue(kvp.Value));

            return _cachedAttributes;
        }
    }

    private static object? ExtractValue(OguFieldValue fv)
    {
        if (fv.IsNull) return null;

        // Return the raw value directly — OguFieldValue stores native types
        var raw = fv.Value;
        if (raw is string or int or long or double or float or bool or DateTime or decimal)
            return raw;

        return fv.GetStringValue();
    }
}
