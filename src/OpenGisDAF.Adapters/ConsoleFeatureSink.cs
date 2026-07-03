using OpenGisDAF.Adapters.Utilities;
using OpenGisDAF.Core;

namespace OpenGisDAF.Adapters;

public sealed class ConsoleFeatureSink : IFeatureSink
{
    private OutputSchema? _schema;
    private int _writtenCount;

    public Task InitializeAsync(OutputSchema schema, CancellationToken cancellationToken = default)
    {
        _schema = schema;
        _writtenCount = 0;
        Console.WriteLine("=== Console Output ===");
        Console.WriteLine($"Schema: {schema.Description ?? "Unnamed"}");
        if (schema.ProducedFields.Count > 0)
        {
            Console.WriteLine($"Fields: {string.Join(", ", schema.ProducedFields.Select(f => $"{f.Name}:{f.Type}"))}");
        }
        return Task.CompletedTask;
    }

    public Task WriteAsync(IFeature feature, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _writtenCount++;

        Console.WriteLine($"--- Feature {feature.Id} ---");
        Console.WriteLine($"  Geometry: {WktConverter.ToWkt(feature.Geometry)}");
        foreach (var attr in feature.Attributes)
        {
            Console.WriteLine($"  {attr.Key}: {attr.Value ?? "<null>"}");
        }

        return Task.CompletedTask;
    }

    public async Task WriteBatchAsync(IAsyncEnumerable<IFeature> features, CancellationToken cancellationToken = default)
    {
        await foreach (var feature in features.WithCancellation(cancellationToken))
        {
            await WriteAsync(feature, cancellationToken);
        }
    }

    public Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"--- Total features written: {_writtenCount} ---");
        Console.WriteLine("=== Console Output Complete ===");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
