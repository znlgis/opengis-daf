namespace OpenGisDAF.Scheduling;

public sealed class GlobalConcurrencyController : IDisposable
{
    private readonly SemaphoreSlim _semaphore;

    public GlobalConcurrencyController(int maxConcurrency = 1)
    {
        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public Task WaitAsync(CancellationToken cancellationToken = default)
    {
        return _semaphore.WaitAsync(cancellationToken);
    }

    public void Release()
    {
        _semaphore.Release();
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
