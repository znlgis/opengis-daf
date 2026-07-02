namespace OpenGisDAF.Core;

public interface IFeatureSource : IAsyncDisposable
{
    FeatureSourceMetadata Metadata { get; }
    NetTopologySuite.Geometries.Envelope BoundingBox { get; }
    ISpatialReference SpatialReference { get; }
    Task<long> GetFeatureCountAsync();
    IAsyncEnumerable<IFeature> GetFeaturesAsync(
        NetTopologySuite.Geometries.Envelope? boundingBox = null,
        string? filterExpression = null,
        CancellationToken cancellationToken = default);
}
