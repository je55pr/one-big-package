namespace OBP.Game;

/// <summary>View work is canceled on close; an admitted save remains supervised until its writer commits or fails.</summary>
internal sealed class BugReportUiWork(HostOperationScope host) : IDisposable
{
    private CancellationTokenSource? _view;
    private long _version;
    private bool _disposed;

    internal void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Close();
        _view = new();
    }

    internal void Start<T>(string name, Func<CancellationToken, Task<T>> request, Action<T> result,
        Action<Exception> failure, bool committedSave = false)
    {
        if (_disposed || host.Stopping || _view is not { } view) return;
        long version = _version;
        host.Start(name, async token =>
        {
            try
            {
                var value = await request(token);
                if (Current(version)) result(value);
            }
            catch (OperationCanceledException error) when (error.CancellationToken == token && token.IsCancellationRequested)
            { /* Closing the view retires its IO/result callbacks. */ }
            catch (Exception error)
            {
                if (Current(version)) failure(error);
                else throw;
            }
        }, committedSave ? CancellationToken.None : view.Token);
    }

    private bool Current(long version) => !_disposed && !host.Stopping && _view is not null && version == _version;

    internal void Close()
    {
        _version++;
        _view?.Cancel();
        _view?.Dispose();
        _view = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
    }
}
