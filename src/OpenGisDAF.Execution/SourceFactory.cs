using Microsoft.Extensions.Logging;
using OpenGisDAF.Adapters;
using OpenGisDAF.Core;

namespace OpenGisDAF.Execution;

public sealed class SourceFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public SourceFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public IFeatureSource CreateSource(InputBinding binding)
    {
        var sourceId = binding.SourceId;
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
