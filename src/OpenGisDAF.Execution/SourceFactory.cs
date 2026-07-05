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

#pragma warning disable CA1822 // 成员不访问实例数据 — 保留实例方法签名以供未来扩展
    public void DisposeSource(IFeatureSource source)
    {
#pragma warning disable CA2012 // 在此 fire-and-forget 场景中故意不等待 ValueTask
        if (source is IAsyncDisposable ad)
            _ = ad.DisposeAsync();
#pragma warning restore CA2012
    }
#pragma warning restore CA1822
}
