using System.Text;
using Microsoft.Extensions.Logging;
using OpenGIS.Utils.Engine.Model.Layer;
using OpenGIS.Utils.Engine.Util;
using OpenGisDAF.Adapters.Utilities;
using OpenGisDAF.Core;

namespace OpenGisDAF.Adapters;

public sealed class ShapefileFeatureSink : IFeatureSink
{
    private readonly OutputBinding _binding;
    private readonly ILogger<ShapefileFeatureSink> _logger;

    private OutputSchema? _schema;
    private readonly List<IFeature> _features = new();

    public ShapefileFeatureSink(OutputBinding binding, ILogger<ShapefileFeatureSink> logger)
    {
        _binding = binding ?? throw new ArgumentNullException(nameof(binding));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task InitializeAsync(OutputSchema schema, CancellationToken cancellationToken = default)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _features.Clear();
        return Task.CompletedTask;
    }

    public Task WriteAsync(IFeature feature, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(feature);

        _features.Add(feature);
        return Task.CompletedTask;
    }

    public async Task WriteBatchAsync(
        IAsyncEnumerable<IFeature> features,
        CancellationToken cancellationToken = default)
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

        if (_schema is null)
            throw new InvalidOperationException("Sink not initialized. Call InitializeAsync first.");

        var layer = BuildOguLayer();
        var encoding = ResolveEncoding();

        var outputDir = Path.GetDirectoryName(_binding.TargetPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        ShpUtil.WriteShapefile(layer, _binding.TargetPath, encoding);

        _logger.LogInformation(
            "Shapefile written: {Path}, {Count} features, encoding: {Encoding}",
            _binding.TargetPath, _features.Count, encoding.WebName);

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _features.Clear();
        _schema = null;
        return ValueTask.CompletedTask;
    }

    private OguLayer BuildOguLayer()
    {
        var schema = _schema!;
        var layer = new OguLayer
        {
            Name = schema.Description ?? Path.GetFileNameWithoutExtension(_binding.TargetPath),
            GeometryType = GeometryTypeMapper.ToOguGeometryType(schema.ProducedGeometryType)
        };

        foreach (var fieldDef in schema.ProducedFields)
        {
            layer.Fields.Add(new OguField
            {
                Name = fieldDef.Name,
                DataType = FieldTypeMapper.ToFieldDataType(fieldDef.Type),
                IsNullable = !fieldDef.Required
            });
        }

        foreach (var feature in _features)
        {
            var oguFeature = new OguFeature
            {
                Wkt = WktConverter.ToWkt(feature.Geometry)
            };

            foreach (var attr in feature.Attributes)
            {
                oguFeature.Attributes[attr.Key] = new OguFieldValue(attr.Value);
            }

            layer.Features.Add(oguFeature);
        }

        return layer;
    }

    private Encoding ResolveEncoding()
    {
        var encodingName = _binding.FormatOptions?.Encoding;
        if (string.IsNullOrWhiteSpace(encodingName))
            return Encoding.UTF8;

        try
        {
            return Encoding.GetEncoding(encodingName);
        }
        catch (ArgumentException)
        {
            _logger.LogWarning(
                "Unsupported encoding '{Encoding}', falling back to UTF-8",
                encodingName);
            return Encoding.UTF8;
        }
    }


}
