using Microsoft.Extensions.Logging;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGisDAF.Adapters.Utilities;
using OpenGisDAF.Core;

namespace OpenGisDAF.Adapters;

public sealed class PostgisFeatureSource : IFeatureSource
{
    private readonly ConnectionConfig _config;
    private readonly string _tableName;
    private readonly ILogger<PostgisFeatureSource> _logger;
    private readonly IConnectionEncryption _encryption;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private OguLayer? _baseLayer;
    private Envelope? _boundingBox;
    private ISpatialReference? _spatialReference;

    public PostgisFeatureSource(
        ConnectionConfig config,
        string tableName,
        ILogger<PostgisFeatureSource> logger,
        IConnectionEncryption encryption)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));

        Metadata = new FeatureSourceMetadata
        {
            SourceId = config.DataSourceId,
            SourceType = "PostGIS"
        };
    }

    public FeatureSourceMetadata Metadata { get; private set; }

    public Envelope BoundingBox => _boundingBox ?? new Envelope();

    public ISpatialReference SpatialReference =>
        _spatialReference ?? throw new InvalidOperationException(
            "SpatialReference is not available until the layer has been loaded.");

    public async Task<long> GetFeatureCountAsync()
    {
        await EnsureBaseLayerLoadedAsync(CancellationToken.None);
        return _baseLayer!.Features.Count;
    }

    public async IAsyncEnumerable<IFeature> GetFeaturesAsync(
        Envelope? boundingBox = null,
        string? filterExpression = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Always ensure base layer is loaded first so metadata (BoundingBox,
        // SpatialReference, FeatureCount) is available even for filtered queries.
        await EnsureBaseLayerLoadedAsync(cancellationToken);

        OguLayer layer;

        if (boundingBox is null && string.IsNullOrWhiteSpace(filterExpression))
        {
            layer = _baseLayer!;
        }
        else
        {
            var spatialFilter = boundingBox is not null ? BuildSpatialFilterWkt(boundingBox) : null;
            layer = await LoadLayerAsync(
                string.IsNullOrWhiteSpace(filterExpression) ? null : filterExpression,
                spatialFilter,
                cancellationToken);
        }

        await Task.Yield();

        foreach (var feature in layer.Features)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new OguFeatureWrapper(feature);
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            _lock.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed or in the process of being disposed
        }

        _baseLayer = null;
        _boundingBox = null;
        _spatialReference = null;
        return ValueTask.CompletedTask;
    }

    private async Task EnsureBaseLayerLoadedAsync(CancellationToken cancellationToken)
    {
        if (_baseLayer is not null) return;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_baseLayer is not null) return;

            _baseLayer = await LoadLayerAsync(attributeFilter: null, spatialFilterWkt: null, cancellationToken);
            _boundingBox = ComputeBoundingBox(_baseLayer);

            if (_baseLayer.Wkid.HasValue)
            {
                _spatialReference = new Utilities.SpatialReference(
                    _baseLayer.Wkid.Value,
                    Utilities.SpatialReference.BuildWkt(_baseLayer.Wkid.Value));
            }

            Metadata = Metadata with { FeatureCount = _baseLayer.Features.Count };
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<OguLayer> LoadLayerAsync(
        string? attributeFilter,
        string? spatialFilterWkt,
        CancellationToken cancellationToken = default)
    {
        var connectionString = BuildConnectionString();

        _logger.LogDebug(
            "Loading PostGIS layer '{TableName}' from database '{Database}' on {Host}:{Port}",
            _tableName, _config.Database, _config.Host, _config.Port);

        var layer = await OguLayerUtil.ReadLayerAsync(
            DataFormatType.POSTGIS,
            connectionString,
            layerName: _tableName,
            attributeFilter: attributeFilter,
            spatialFilterWkt: spatialFilterWkt).WaitAsync(cancellationToken);

        _logger.LogDebug(
            "Loaded {FeatureCount} features from PostGIS layer '{TableName}'",
            layer.Features.Count, _tableName);

        return layer;
    }

    private string BuildConnectionString()
    {
        var password = _encryption.Decrypt(_config.EncryptedPassword ?? string.Empty);
        var builder = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = _config.Host,
            Port = _config.Port,
            Database = _config.Database,
            Username = _config.UserName,
            Password = password
        };

        return $"PG:host='{EscapePgValue(builder.Host!)}' port={builder.Port} dbname='{EscapePgValue(builder.Database!)}' user='{EscapePgValue(builder.Username!)}' password='{EscapePgValue(password)}'";
    }

    private static string EscapePgValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Replace("\\", "\\\\").Replace("'", "\\'");
    }

    private static string BuildSpatialFilterWkt(Envelope envelope)
    {
        var factory = NtsGeometryServices.Instance.CreateGeometryFactory();
        var polygon = factory.ToGeometry(envelope);
        return WktConverter.ToWkt(polygon);
    }

    private static Envelope ComputeBoundingBox(OguLayer layer)
    {
        if (layer.Features.Count == 0)
            return new Envelope();

        var env = new Envelope();
        foreach (var feature in layer.Features)
        {
            if (feature.Wkt is null) continue;

            var geom = WktConverter.FromWkt(feature.Wkt);
            env.ExpandToInclude(geom.EnvelopeInternal);
        }

        return env;
    }
}
