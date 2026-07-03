using Microsoft.Extensions.Logging;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGisDAF.Adapters.Utilities;
using OpenGisDAF.Core;

namespace OpenGisDAF.Adapters;

public sealed class PostgisFeatureSink : IFeatureSink
{
    private readonly OutputBinding _binding;
    private readonly ILogger<PostgisFeatureSink> _logger;
    private readonly IConnectionEncryption _encryption;
    private OutputSchema? _schema;
    private List<OguFeature>? _features;
    private int _fidCounter;

    public PostgisFeatureSink(
        OutputBinding binding,
        ILogger<PostgisFeatureSink> logger,
        IConnectionEncryption encryption)
    {
        _binding = binding ?? throw new ArgumentNullException(nameof(binding));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
    }

    public Task InitializeAsync(OutputSchema schema, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _schema = schema;
        _features = new List<OguFeature>();
        _fidCounter = 0;

        _logger.LogDebug(
            "PostGIS sink initialized for table '{TableName}', {FieldCount} fields",
            _binding.TargetPath, schema.ProducedFields.Count);

        return Task.CompletedTask;
    }

    public Task WriteAsync(IFeature feature, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_features is null)
            throw new InvalidOperationException("Sink must be initialized before writing.");

        var oguFeature = new OguFeature
        {
            Fid = ++_fidCounter,
            Wkt = feature.Geometry is not null ? WktConverter.ToWkt(feature.Geometry) : null
        };

        foreach (var (key, value) in feature.Attributes)
        {
            oguFeature.Attributes[key] = new OguFieldValue(value);
        }

        _features.Add(oguFeature);
        return Task.CompletedTask;
    }

    public async Task WriteBatchAsync(
        IAsyncEnumerable<IFeature> features,
        CancellationToken cancellationToken = default)
    {
        await foreach (var feature in features.WithCancellation(cancellationToken))
        {
            await WriteAsync(feature, cancellationToken);
        }
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_features is null)
            throw new InvalidOperationException("Sink must be initialized before completing.");

        if (_binding.ConnectionConfig is null)
            throw new InvalidOperationException("OutputBinding must have a ConnectionConfig for PostGIS sink.");

        var layer = new OguLayer
        {
            Name = _binding.TargetPath,
            GeometryType = MapGeometryType(_schema?.ProducedGeometryType),
            Fields = BuildFieldDefinitions()
        };

        foreach (var feature in _features)
        {
            layer.Features.Add(feature);
        }

        var connectionString = BuildConnectionString(_binding.ConnectionConfig);

        _logger.LogInformation(
            "Writing {FeatureCount} features to PostGIS table '{TableName}'",
            _features.Count, _binding.TargetPath);

        await OguLayerUtil.WriteLayerAsync(
            DataFormatType.POSTGIS,
            layer,
            connectionString,
            layerName: _binding.TargetPath).WaitAsync(cancellationToken);

        _logger.LogInformation(
            "Successfully wrote {FeatureCount} features to PostGIS table '{TableName}'",
            _features.Count, _binding.TargetPath);
    }

    public ValueTask DisposeAsync()
    {
        _features?.Clear();
        _features = null;
        _schema = null;
        return ValueTask.CompletedTask;
    }

    private string BuildConnectionString(ConnectionConfig config)
    {
        var password = _encryption.Decrypt(config.EncryptedPassword ?? string.Empty);
        return $"PG:host={config.Host} port={config.Port} dbname={config.Database} user={config.UserName} password={password}";
    }

    private IList<OguField> BuildFieldDefinitions()
    {
        if (_schema?.ProducedFields is null || _schema.ProducedFields.Count == 0)
            return new List<OguField>();

        var fields = new List<OguField>(_schema.ProducedFields.Count);
        foreach (var fieldDef in _schema.ProducedFields)
        {
            fields.Add(new OguField
            {
                Name = fieldDef.Name,
                DataType = MapFieldType(fieldDef.Type),
                IsNullable = !fieldDef.Required
            });
        }

        return fields;
    }

    private static FieldDataType MapFieldType(FieldType type) => type switch
    {
        FieldType.String => FieldDataType.STRING,
        FieldType.Integer => FieldDataType.INTEGER,
        FieldType.Double => FieldDataType.DOUBLE,
        FieldType.DateTime => FieldDataType.DATETIME,
        FieldType.Boolean => FieldDataType.BOOLEAN,
        FieldType.Geometry => FieldDataType.BINARY,
        _ => FieldDataType.STRING
    };

    private static OpenGIS.Utils.Engine.Enums.GeometryType MapGeometryType(Core.GeometryType? type) => type switch
    {
        Core.GeometryType.Point => OpenGIS.Utils.Engine.Enums.GeometryType.POINT,
        Core.GeometryType.MultiPoint => OpenGIS.Utils.Engine.Enums.GeometryType.MULTIPOINT,
        Core.GeometryType.LineString => OpenGIS.Utils.Engine.Enums.GeometryType.LINESTRING,
        Core.GeometryType.MultiLineString => OpenGIS.Utils.Engine.Enums.GeometryType.MULTILINESTRING,
        Core.GeometryType.Polygon => OpenGIS.Utils.Engine.Enums.GeometryType.POLYGON,
        Core.GeometryType.MultiPolygon => OpenGIS.Utils.Engine.Enums.GeometryType.MULTIPOLYGON,
        Core.GeometryType.GeometryCollection => OpenGIS.Utils.Engine.Enums.GeometryType.GEOMETRYCOLLECTION,
        _ => OpenGIS.Utils.Engine.Enums.GeometryType.UNKNOWN
    };
}
