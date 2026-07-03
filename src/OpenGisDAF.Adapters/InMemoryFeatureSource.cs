using NetTopologySuite.Geometries;
using OpenGisDAF.Adapters.Utilities;
using OpenGisDAF.Core;

namespace OpenGisDAF.Adapters;

public sealed class InMemoryFeatureSource : IFeatureSource
{
    private readonly List<IFeature> _features;
    private readonly FeatureSourceMetadata _metadata;

    public InMemoryFeatureSource(IEnumerable<IFeature> features, string? sourceId = null)
    {
        _features = features.ToList();
        _metadata = new FeatureSourceMetadata
        {
            SourceId = sourceId ?? $"memory_{Guid.NewGuid():N}",
            SourceType = "InMemory",
            FeatureCount = _features.Count,
            Description = "In-memory feature collection"
        };
    }

    public FeatureSourceMetadata Metadata => _metadata;
    public Envelope BoundingBox => CalculateBoundingBox();
    public ISpatialReference SpatialReference => throw new NotSupportedException("InMemory source has no CRS; set via operator configuration.");

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

            if (boundingBox is not null && !boundingBox.Intersects(feature.Geometry.EnvelopeInternal))
                continue;

            yield return feature;
        }
    }

    public ValueTask DisposeAsync()
    {
        _features.Clear();
        return ValueTask.CompletedTask;
    }

    private Envelope CalculateBoundingBox()
    {
        if (_features.Count == 0) return new Envelope();

        var env = new Envelope();
        foreach (var f in _features)
            env.ExpandToInclude(f.Geometry.EnvelopeInternal);

        return env;
    }
}
