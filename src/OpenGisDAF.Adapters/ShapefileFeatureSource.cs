using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGIS.Utils.Engine.Util;
using OpenGisDAF.Adapters.Utilities;
using OpenGisDAF.Core;

namespace OpenGisDAF.Adapters;

public sealed class ShapefileFeatureSource : IFeatureSource
{
    private readonly string _shapefilePath;
    private readonly ILogger<ShapefileFeatureSource> _logger;
    private readonly Lazy<OguLayer> _layer;

    private FeatureSourceMetadata? _cachedMetadata;
    private Envelope? _cachedBoundingBox;
    private ISpatialReference? _cachedSpatialReference;

    public ShapefileFeatureSource(string shapefilePath, ILogger<ShapefileFeatureSource> logger)
    {
        _shapefilePath = shapefilePath ?? throw new ArgumentNullException(nameof(shapefilePath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (!File.Exists(_shapefilePath))
            throw new FileNotFoundException("Shapefile not found", _shapefilePath);

        _layer = new Lazy<OguLayer>(LoadLayer);
    }

    public FeatureSourceMetadata Metadata => _cachedMetadata ??= BuildMetadata();

    public Envelope BoundingBox => _cachedBoundingBox ??= ComputeBoundingBox();

    public ISpatialReference SpatialReference => _cachedSpatialReference ??= BuildSpatialReference();

    public Task<long> GetFeatureCountAsync()
    {
        return Task.FromResult((long)_layer.Value.GetFeatureCount());
    }

    public async IAsyncEnumerable<IFeature> GetFeaturesAsync(
        Envelope? boundingBox = null,
        string? filterExpression = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();

        if (!string.IsNullOrWhiteSpace(filterExpression))
        {
            _logger.LogWarning(
                "ShapefileFeatureSource does not support attribute filtering. " +
                "The filter expression '{FilterExpression}' will be ignored. " +
                "All features from '{Path}' will be returned.",
                filterExpression, _shapefilePath);
        }

        var layer = _layer.Value;
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
                _logger.LogWarning(ex, "Skipping feature {Fid} due to conversion error in {Path}",
                    oguFeature.Fid, _shapefilePath);
                continue;
            }

            yield return feature;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_layer.IsValueCreated && _layer.Value is IDisposable disposable)
            disposable.Dispose();
        _cachedMetadata = null;
        _cachedBoundingBox = null;
        _cachedSpatialReference = null;
        return ValueTask.CompletedTask;
    }

    private OguLayer LoadLayer()
    {
        _logger.LogDebug("Loading Shapefile: {Path}", _shapefilePath);
        // ShpUtil.ReadShapefile internally detects encoding via .cpg file (or falls back to system default)
        return ShpUtil.ReadShapefile(_shapefilePath);
    }

    private FeatureSourceMetadata BuildMetadata()
    {
        var layer = _layer.Value;
        return new FeatureSourceMetadata
        {
            SourceId = Path.GetFileNameWithoutExtension(_shapefilePath),
            SourceType = "Shapefile",
            FeatureCount = layer.GetFeatureCount(),
            Description = $"Shapefile: {_shapefilePath}"
        };
    }

    private Envelope ComputeBoundingBox()
    {
        var layer = _layer.Value;
        var env = new Envelope();

        foreach (var oguFeature in layer.Features)
        {
            if (oguFeature.Wkt is null)
                continue;

            var geom = WktConverter.FromWkt(oguFeature.Wkt);
            env.ExpandToInclude(geom.EnvelopeInternal);
        }

        if (env.IsNull)
            _logger.LogWarning("Shapefile has no valid geometry: {Path}", _shapefilePath);

        return env;
    }

    private ISpatialReference BuildSpatialReference()
    {
        var layer = _layer.Value;
        var wkid = layer.Wkid ?? 0;
        var wkt = ReadPrjFile() ?? string.Empty;

        if (wkid <= 0)
        {
            _logger.LogWarning("No coordinate system found for Shapefile: {Path}", _shapefilePath);
        }

        return new Utilities.SpatialReference(wkid > 0 ? wkid : 0, wkt.Length > 0 ? wkt : "Unknown");
    }

    private string? ReadPrjFile()
    {
        var prjPath = Path.ChangeExtension(_shapefilePath, ".prj");
        return File.Exists(prjPath) ? File.ReadAllText(prjPath) : null;
    }
}
