using Microsoft.Extensions.Logging;
using OpenGisDAF.Adapters;
using OpenGisDAF.Adapters.Utilities;
using OpenGisDAF.Core;

namespace OpenGisDAF.Execution;

public sealed class SinkFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IConnectionEncryption _encryption;

    public SinkFactory(ILoggerFactory loggerFactory, IConnectionEncryption encryption)
    {
        _loggerFactory = loggerFactory;
        _encryption = encryption;
    }

    public IFeatureSink CreateSink(OutputBinding binding)
    {
        var adapterType = binding.AdapterType.ToLowerInvariant();

        return adapterType switch
        {
            "console" => new ConsoleFeatureSink(),
            "geojson" => CreateGeoJsonSink(binding),
            "shapefile" => CreateShapefileSink(binding),
            "postgis" => CreatePostgisSink(binding),
            _ => throw new NotSupportedException(
                $"不支持的目标适配器类型: '{binding.AdapterType}'。支持: console, geojson, shapefile, postgis")
        };
    }

    private GeoJsonFeatureSink CreateGeoJsonSink(OutputBinding binding)
    {
        var logger = _loggerFactory.CreateLogger<GeoJsonFeatureSink>();
        return new GeoJsonFeatureSink(binding, logger);
    }

    private ShapefileFeatureSink CreateShapefileSink(OutputBinding binding)
    {
        var logger = _loggerFactory.CreateLogger<ShapefileFeatureSink>();
        return new ShapefileFeatureSink(binding, logger);
    }

    private PostgisFeatureSink CreatePostgisSink(OutputBinding binding)
    {
        if (binding.ConnectionConfig is null)
            throw new InvalidOperationException("PostGIS 输出需要 ConnectionConfig");

        var logger = _loggerFactory.CreateLogger<PostgisFeatureSink>();
        return new PostgisFeatureSink(binding, logger, _encryption);
    }

#pragma warning disable CA1822 // 成员不访问实例数据 — 保留实例方法签名以供未来扩展
    public static async ValueTask DisposeSinkAsync(IFeatureSink sink)
    {
        await sink.DisposeAsync();
    }
#pragma warning restore CA1822
}
