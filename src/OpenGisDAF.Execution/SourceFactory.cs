using Microsoft.Extensions.Logging;
using OpenGisDAF.Adapters;
using OpenGisDAF.Adapters.Utilities;
using OpenGisDAF.Core;

namespace OpenGisDAF.Execution;

public sealed class SourceFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IConnectionEncryption _encryption;

    public SourceFactory(ILoggerFactory loggerFactory, IConnectionEncryption encryption)
    {
        _loggerFactory = loggerFactory;
        _encryption = encryption;
    }

    public IFeatureSource CreateSource(InputBinding binding)
    {
        var sourceId = binding.SourceId;

        if (binding.ConnectionConfig is not null)
        {
            if (!string.Equals(binding.ConnectionConfig.AdapterType, "postgis", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(binding.ConnectionConfig.AdapterType, "postgiswriter", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("结构化输入的 AdapterType 必须为 postgis。", nameof(binding));
            }

            return new PostgisFeatureSource(
                binding.ConnectionConfig,
                sourceId,
                _loggerFactory.CreateLogger<PostgisFeatureSource>(),
                _encryption);
        }

        var extension = Path.GetExtension(sourceId)?.ToLowerInvariant();

        return extension switch
        {
            ".shp" => CreateShapefileSource(sourceId),
            ".geojson" or ".json" => CreateGeoJsonSource(sourceId),
            _ => throw new NotSupportedException(
                $"无法识别的数据源类型: '{sourceId}'。支持的格式: .shp, .geojson, .json")
        };
    }

    private ShapefileFeatureSource CreateShapefileSource(string path)
    {
        var logger = _loggerFactory.CreateLogger<ShapefileFeatureSource>();
        return new ShapefileFeatureSource(path, logger);
    }

    private static GeoJsonFeatureSource CreateGeoJsonSource(string path)
    {
        return new GeoJsonFeatureSource(path);
    }
}
