using NetTopologySuite.Geometries;
using OpenGisDAF.Core;

namespace OpenGisDAF.SamplePlugin;

/// <summary>
/// 插件自带的内存要素源，实现 <c>OpenGisDAF.Core.IFeatureSource</c> 契约。
/// 故意不实现 <c>IFeatureSchemaProvider</c>：写出时框架会自动物化并推断
/// 字段并集、几何类型与 CRS，这是第三方算子的最小接入路径。
/// </summary>
public sealed class PluginFeatureSource : IFeatureSource
{
    private readonly List<IFeature> _features;
    private Envelope? _cachedEnvelope;

    public PluginFeatureSource(IEnumerable<IFeature> features)
    {
        _features = [.. features];
    }

    public FeatureSourceMetadata Metadata { get; } = new()
    {
        SourceId = "plugin_length_source",
        SourceType = "PluginMemory",
        FeatureCount = 0,
        Description = "示例插件产出的内存要素集合"
    };

    public Envelope BoundingBox
    {
        get
        {
            if (_cachedEnvelope is not null) return _cachedEnvelope;
            var env = new Envelope();
            foreach (var feature in _features)
            {
                if (feature.Geometry is not null)
                    env.ExpandToInclude(feature.Geometry.EnvelopeInternal);
            }
            _cachedEnvelope = env;
            return env;
        }
    }

    public ISpatialReference SpatialReference { get; } = new UnknownSpatialReference();

    public Task<long> GetFeatureCountAsync() => Task.FromResult((long)_features.Count);

    public async IAsyncEnumerable<IFeature> GetFeaturesAsync(
        Envelope? boundingBox = null,
        string? filterExpression = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();

        foreach (var feature in _features)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (boundingBox is not null && feature.Geometry is not null &&
                !boundingBox.Intersects(feature.Geometry.EnvelopeInternal))
                continue;

            yield return feature;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// 未知空间参考的占位实现：第三方算子不感知 CRS 时使用，
/// 框架对未知 CRS 保持未知，不会伪造 EPSG:4326。
/// </summary>
public sealed class UnknownSpatialReference : ISpatialReference
{
    public string Authority => "UNKNOWN";
    public int Code => 0;
    public string Wkt => string.Empty;

    public bool IsEquivalentTo(ISpatialReference other) => ReferenceEquals(this, other);
}
