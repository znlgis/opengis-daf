namespace OpenGisDAF.Core;

public interface IFeatureSink
{
    Task InitializeAsync(OutputSchema schema, CancellationToken cancellationToken);
    Task WriteAsync(IFeature feature, CancellationToken cancellationToken);
    Task WriteBatchAsync(IAsyncEnumerable<IFeature> features, CancellationToken cancellationToken);
    Task CompleteAsync(CancellationToken cancellationToken);
}
