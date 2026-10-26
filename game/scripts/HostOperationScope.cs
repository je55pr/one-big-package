using System.Collections.Concurrent;

namespace OBP.Game;

public sealed record HostOperationFailure(string Name, Exception Error, bool Superseded);

/// <summary>Own critical host work until completion or root shutdown. Failures are drained by the host.</summary>
public sealed class HostOperationScope : IDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HashSet<Task> _pending = [];
    private readonly ConcurrentQueue<HostOperationFailure> _failures = new();
    private TaskCompletionSource? _stopped;

    public CancellationToken Token { get; }
    public bool Stopping { get { lock (_sync) return _stopped is not null; } }
    public int PendingCount { get { lock (_sync) return _pending.Count; } }

    public HostOperationScope() => Token = _shutdown.Token;

    /// <summary>The optional token narrows work to a UI or attachment lifetime; destination changes alone do not cancel root work.</summary>
    public void Start(string name, Func<CancellationToken, Task> work, CancellationToken lifetime = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(work);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenSource cancellation;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_stopped is not null, this);
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token, lifetime);
            _pending.Add(completion.Task);
        }
        _ = ObserveAsync(name, work, cancellation, completion);
    }

    private async Task ObserveAsync(string name, Func<CancellationToken, Task> work,
        CancellationTokenSource cancellation, TaskCompletionSource completion)
    {
        var token = cancellation.Token;
        try { await work(token); }
        catch (OperationCanceledException error) when (error.CancellationToken == token && token.IsCancellationRequested) { }
        catch (Exception error) { _failures.Enqueue(new(name, error, token.IsCancellationRequested)); }
        finally
        {
            cancellation.Dispose();
            lock (_sync) _pending.Remove(completion.Task);
            completion.TrySetResult();
        }
    }

    public IReadOnlyList<HostOperationFailure> TakeFailures()
    {
        var result = new List<HostOperationFailure>();
        while (_failures.TryDequeue(out var failure)) result.Add(failure);
        return result;
    }

    /// <summary>Idempotently cancel, await all admitted work and release cancellation resources.</summary>
    public Task StopAsync()
    {
        Task[] pending;
        TaskCompletionSource stopped;
        lock (_sync)
        {
            if (_stopped is { } existing) return existing.Task;
            _stopped = stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pending = _pending.ToArray();
        }
        try { _shutdown.Cancel(); }
        catch (AggregateException error) { _failures.Enqueue(new("host-cancellation", error, true)); }
        _ = FinishStopAsync(pending, stopped);
        return stopped.Task;
    }

    private async Task FinishStopAsync(Task[] pending, TaskCompletionSource stopped)
    {
        await Task.WhenAll(pending);
        _shutdown.Dispose();
        stopped.TrySetResult();
    }

    public void Dispose() => _ = StopAsync();
}
