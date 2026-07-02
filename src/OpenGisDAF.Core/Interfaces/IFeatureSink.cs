namespace OpenGisDAF.Core;

public interface IFeatureSink : IAsyncDisposable
{
    Task InitializeAsync(OutputSchema schema, CancellationToken cancellationToken = default);
    Task WriteAsync(IFeature feature, CancellationToken cancellationToken = default);
    Task WriteBatchAsync(IAsyncEnumerable<IFeature> features, CancellationToken cancellationToken = default);
    Task CompleteAsync(CancellationToken cancellationToken = default);
}
