using Microsoft.Extensions.Logging;
using OpenGIS.Utils.DataSource;
using OpenGIS.Utils.Engine.Enums;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGisDAF.Adapters.Utilities;
using OpenGisDAF.Core;

namespace OpenGisDAF.Adapters;

public sealed class GeoJsonFeatureSink : IFeatureSink
{
    private readonly OutputBinding _outputBinding;
    private readonly ILogger<GeoJsonFeatureSink>? _logger;
    private OutputSchema? _schema;
    private readonly List<IFeature> _features = new();
    private bool _completed;

    public GeoJsonFeatureSink(OutputBinding outputBinding, ILogger<GeoJsonFeatureSink>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(outputBinding);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputBinding.TargetPath);

        _outputBinding = outputBinding;
        _logger = logger;
    }

    public Task InitializeAsync(OutputSchema schema, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schema);
        cancellationToken.ThrowIfCancellationRequested();

        _schema = schema;
        _features.Clear();
        _completed = false;

        _logger?.LogDebug("GeoJsonFeatureSink initialized for {Path} with {FieldCount} fields",
            _outputBinding.TargetPath, schema.ProducedFields.Count);

        return Task.CompletedTask;
    }

    public Task WriteAsync(IFeature feature, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feature);
        cancellationToken.ThrowIfCancellationRequested();

        _features.Add(feature);
        return Task.CompletedTask;
    }

    public async Task WriteBatchAsync(IAsyncEnumerable<IFeature> features, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(features);

        await foreach (var feature in features.WithCancellation(cancellationToken))
        {
            _features.Add(feature);
        }
    }

    public Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_completed)
            return Task.CompletedTask;

        if (_schema is null)
            throw new InvalidOperationException("Sink has not been initialized. Call InitializeAsync before CompleteAsync.");

        _completed = true;

        var oguLayer = BuildOguLayer();
        var options = BuildWriteOptions();

        var targetPath = _outputBinding.TargetPath;
        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);

        _logger?.LogInformation("Writing {Count} features to GeoJSON at {Path}",
            oguLayer.Features.Count, targetPath);

        OguLayerUtil.WriteLayer(DataFormatType.GEOJSON, oguLayer, targetPath, options: options);

        _logger?.LogInformation("GeoJSON written successfully to {Path}", targetPath);

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _features.Clear();
        return ValueTask.CompletedTask;
    }

    private OguLayer BuildOguLayer()
    {
        var layer = new OguLayer
        {
            Name = Path.GetFileNameWithoutExtension(_outputBinding.TargetPath),
            Wkid = 4326,
            GeometryType = OpenGIS.Utils.Engine.Enums.GeometryType.UNKNOWN
        };

        var schema = _schema!;

        if (schema.ProducedGeometryType is not null)
            layer.GeometryType = MapToOguGeometryType(schema.ProducedGeometryType.Value);

        var declaredFieldNames = schema.ProducedFields
            .Where(f => f.Type != FieldType.Geometry)
            .Select(f => f.Name)
            .ToHashSet(StringComparer.Ordinal);

        var hasDeclaredFields = declaredFieldNames.Count > 0;

        if (hasDeclaredFields)
        {
            foreach (var fieldDef in schema.ProducedFields)
            {
                if (fieldDef.Type == FieldType.Geometry) continue;

                layer.Fields.Add(new OguField
                {
                    Name = fieldDef.Name,
                    DataType = MapToFieldDataType(fieldDef.Type),
                    IsNullable = !fieldDef.Required
                });
            }
        }

        var attrNames = new HashSet<string>(StringComparer.Ordinal);

        int fid = 0;
        foreach (var feature in _features)
        {
            var oguFeature = new OguFeature
            {
                Fid = fid++,
                Wkt = WktConverter.ToWkt(feature.Geometry)
            };

            foreach (var attr in feature.Attributes)
            {
                if (hasDeclaredFields && !declaredFieldNames.Contains(attr.Key)) continue;
                oguFeature.Attributes[attr.Key] = new OguFieldValue(attr.Value);
                attrNames.Add(attr.Key);
            }

            layer.Features.Add(oguFeature);
        }

        if (!hasDeclaredFields)
        {
            foreach (var name in attrNames)
            {
                layer.Fields.Add(new OguField
                {
                    Name = name,
                    DataType = FieldDataType.STRING,
                    IsNullable = true
                });
            }
        }

        return layer;
    }

    private Dictionary<string, object>? BuildWriteOptions()
    {
        var fmt = _outputBinding.FormatOptions;
        if (fmt is null) return null;

        var options = new Dictionary<string, object>();

        if (fmt.DecimalPlaces.HasValue)
            options["decimal_places"] = fmt.DecimalPlaces.Value;

        if (!string.IsNullOrEmpty(fmt.DateFormat))
            options["date_format"] = fmt.DateFormat;

        if (!string.IsNullOrEmpty(fmt.Encoding))
            options["encoding"] = fmt.Encoding;

        return options.Count > 0 ? options : null;
    }

    private static FieldDataType MapToFieldDataType(FieldType fieldType)
    {
        return fieldType switch
        {
            FieldType.String => FieldDataType.STRING,
            FieldType.Integer => FieldDataType.INTEGER,
            FieldType.Double => FieldDataType.DOUBLE,
            FieldType.DateTime => FieldDataType.DATETIME,
            FieldType.Boolean => FieldDataType.BOOLEAN,
            _ => FieldDataType.STRING
        };
    }

    private static OpenGIS.Utils.Engine.Enums.GeometryType MapToOguGeometryType(
        OpenGisDAF.Core.GeometryType geometryType)
    {
        return geometryType switch
        {
            OpenGisDAF.Core.GeometryType.Point => OpenGIS.Utils.Engine.Enums.GeometryType.POINT,
            OpenGisDAF.Core.GeometryType.MultiPoint => OpenGIS.Utils.Engine.Enums.GeometryType.MULTIPOINT,
            OpenGisDAF.Core.GeometryType.LineString => OpenGIS.Utils.Engine.Enums.GeometryType.LINESTRING,
            OpenGisDAF.Core.GeometryType.MultiLineString => OpenGIS.Utils.Engine.Enums.GeometryType.MULTILINESTRING,
            OpenGisDAF.Core.GeometryType.Polygon => OpenGIS.Utils.Engine.Enums.GeometryType.POLYGON,
            OpenGisDAF.Core.GeometryType.MultiPolygon => OpenGIS.Utils.Engine.Enums.GeometryType.MULTIPOLYGON,
            OpenGisDAF.Core.GeometryType.GeometryCollection => OpenGIS.Utils.Engine.Enums.GeometryType.GEOMETRYCOLLECTION,
            _ => OpenGIS.Utils.Engine.Enums.GeometryType.UNKNOWN
        };
    }
}
