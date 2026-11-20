using ClaudeBridge.Configuration;

namespace ClaudeBridge.Runs;

/// <summary>
/// Begrenzt, wie viele Claude-Läufe gleichzeitig aktiv sein dürfen (Bridge:MaxConcurrentRuns).
/// </summary>
public sealed class RunConcurrencyLimiter(BridgeOptions options) : IDisposable
{
    private readonly SemaphoreSlim _slots = new(Math.Max(1, options.MaxConcurrentRuns));

    /// <summary>
    /// Wartet auf einen freien Slot. Der Slot wird beim Dispose des Rückgabewerts wieder freigegeben.
    /// </summary>
    public async Task<IDisposable> AcquireSlotAsync(CancellationToken cancellationToken)
    {
        await _slots.WaitAsync(cancellationToken);
        return new Slot(_slots);
    }

    public void Dispose() => _slots.Dispose();

    private sealed class Slot(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                slots.Release();
        }
    }
}
