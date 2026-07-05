using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGisDAF.Adapters.Utilities;
using OpenGisDAF.Core;

namespace OpenGisDAF.Adapters;

public sealed class GeoJsonFeatureSource : IFeatureSource
{
    private readonly string _geojsonPath;
    private readonly ILogger<GeoJsonFeatureSource>? _logger;
    private readonly object _loadLock = new();
    private readonly ConcurrentDictionary<string, OguLayer> _layerCache = new();
    private Envelope? _cachedBoundingBox;
    private ISpatialReference? _cachedSpatialReference;
    private FeatureSourceMetadata? _cachedMetadata;

    public GeoJsonFeatureSource(string geojsonPath, ILogger<GeoJsonFeatureSource>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(geojsonPath);
        _geojsonPath = geojsonPath;
        _logger = logger;
    }

    public FeatureSourceMetadata Metadata
    {
        get
        {
            if (_cachedMetadata is not null) return _cachedMetadata;

            var layer = GetLayer(null);
            _cachedMetadata = new FeatureSourceMetadata
            {
                SourceId = Path.GetFileNameWithoutExtension(_geojsonPath),
                SourceType = "GeoJSON",
                FeatureCount = layer.Features.Count,
                Description = $"GeoJSON source: {_geojsonPath}"
            };
            return _cachedMetadata;
        }
    }

    public Envelope BoundingBox
    {
        get
        {
            if (_cachedBoundingBox is not null) return _cachedBoundingBox;

            var layer = GetLayer(null);
            if (layer.Features.Count == 0)
            {
                _cachedBoundingBox = new Envelope();
                return _cachedBoundingBox;
            }

            var env = new Envelope();
            foreach (var feature in layer.Features)
            {
                if (feature.Wkt is null) continue;
                try
                {
                    var geom = WktConverter.FromWkt(feature.Wkt);
                    env.ExpandToInclude(geom.EnvelopeInternal);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to parse geometry for feature {Fid} in {Path}",
                        feature.Fid, _geojsonPath);
                }
            }

            _cachedBoundingBox = env;
            return _cachedBoundingBox;
        }
    }

    public ISpatialReference SpatialReference
    {
        get
        {
            if (_cachedSpatialReference is not null) return _cachedSpatialReference;

            var layer = GetLayer(null);
            var wkid = layer.Wkid ?? 4326;
            _cachedSpatialReference = new Utilities.SpatialReference(wkid, Utilities.SpatialReference.BuildWkt(wkid));
            return _cachedSpatialReference;
        }
    }

    public Task<long> GetFeatureCountAsync()
    {
        var layer = GetLayer(null);
        return Task.FromResult((long)layer.Features.Count);
    }

    public async IAsyncEnumerable<IFeature> GetFeaturesAsync(
        Envelope? boundingBox = null,
        string? filterExpression = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();

        var layer = GetLayer(filterExpression);

        foreach (var oguFeature in layer.Features)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IFeature feature;
            try
            {
                var wrapper = new OguFeatureWrapper(oguFeature);
                if (boundingBox is not null && !boundingBox.Intersects(wrapper.Geometry.EnvelopeInternal))
                    continue;

                feature = wrapper;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Skipping feature {Fid} due to conversion error in {Path}",
                    oguFeature.Fid, _geojsonPath);
                continue;
            }

            yield return feature;
        }
    }

    public ValueTask DisposeAsync()
    {
        _layerCache.Clear();
        _cachedBoundingBox = null;
        _cachedSpatialReference = null;
        _cachedMetadata = null;
        return ValueTask.CompletedTask;
    }

    private OguLayer GetLayer(string? filterExpression)
    {
        var cacheKey = filterExpression ?? string.Empty;

        if (_layerCache.TryGetValue(cacheKey, out var cached)) return cached;

        lock (_loadLock)
        {
            if (_layerCache.TryGetValue(cacheKey, out cached)) return cached;

            _logger?.LogDebug("Loading GeoJSON from {Path} with filter '{Filter}'", _geojsonPath, filterExpression);
            var layer = OguLayerUtil.ReadLayer(
                DataFormatType.GEOJSON,
                _geojsonPath,
                attributeFilter: filterExpression);

            _logger?.LogInformation("Loaded {Count} features from {Path} with filter '{Filter}'",
                layer.Features.Count, _geojsonPath, filterExpression);

            _layerCache[cacheKey] = layer;
            return layer;
        }
    }

}
