namespace DictaClone.Mac.Lifecycle;

public sealed class MacSingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private bool _ownsMutex;

    private MacSingleInstanceGuard(Mutex mutex, bool ownsMutex)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
    }

    public static bool TryAcquire(out MacSingleInstanceGuard? guard)
    {
        var mutex = new Mutex(
            initiallyOwned: true,
            "DictaClone.Desktop.macOS",
            out bool createdNew);
        guard = createdNew
            ? new MacSingleInstanceGuard(mutex, ownsMutex: true)
            : null;
        if (!createdNew)
        {
            mutex.Dispose();
        }

        return createdNew;
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }

        _mutex.Dispose();
    }
}
