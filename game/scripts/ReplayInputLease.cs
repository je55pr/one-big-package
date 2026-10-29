namespace OBP.Game;

/// <summary>A retained callback cannot dispatch into a retired player or replay owner.</summary>
internal sealed class ReplayInputLease<T>(Func<bool> current, Func<T, T> dispatch,
    Action<Func<T, T>> detach) : IDisposable where T : struct
{
    private bool _disposed;
    internal T Filter(T input) => !_disposed && current() ? dispatch(input) : default;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        detach(Filter);
    }
}
