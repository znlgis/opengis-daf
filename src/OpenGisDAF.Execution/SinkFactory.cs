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
        return binding.AdapterType switch
        {
            OutputAdapterType.ConsoleWriter => new ConsoleFeatureSink(),
            OutputAdapterType.GeoJsonWriter => CreateGeoJsonSink(binding),
            OutputAdapterType.ShapefileWriter => CreateShapefileSink(binding),
            OutputAdapterType.PostGISWriter => CreatePostgisSink(binding),
            _ => throw new NotSupportedException(
                $"不支持的目标适配器类型: '{binding.AdapterType}'。支持: ConsoleWriter, GeoJsonWriter, ShapefileWriter, PostGISWriter")
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

    public static async ValueTask DisposeSinkAsync(IFeatureSink sink)
    {
        await sink.DisposeAsync();
    }
}
