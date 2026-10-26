using OBP.Core;
using OBP.Fusion;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>Host scene ownership around Fusion's existing entry policy. Called on the host thread.</summary>
public sealed class WorldSceneLifetime<TResources>(Action retireBindings,
    Action<WorldSceneLease<TResources>> activateBindings, Action releaseEntryLease) : IWorldEntryHost, IDisposable
    where TResources : class, IDisposable
{
    private WorldSceneLease<TResources>? _prepared;
    private CancellationTokenSource? _preparation;
    private bool _activating;
    private bool _disposed;

    public WorldSceneLease<TResources>? Current { get; private set; }
    public Task PendingPreparation { get; private set; } = Task.CompletedTask;
    public string? DestinationId { get; private set; }
    public ObpDestination? NativeDestination { get; private set; }
    public RuntimeSpawn? PlayerEntry => Current?.PlayerEntry;
    public float EntrySnapReach => Current?.EntrySnapReach ?? 8;

    public IDisposable Prepare(WorldEntryPlan plan, ObpDestination? destination, FusionLabDefinition? lab,
        float snapReach, Func<TResources> prepare)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activating) throw new InvalidOperationException("A world is already being activated.");
        DiscardPrepared();
        var resources = prepare();
        _prepared = new(plan, destination, lab, snapReach, resources, Retired);
        return new Preparation(this, _prepared);
    }

    public IDisposable Activate(WorldEntryPlan plan)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activating || _prepared is not { } next || !ReferenceEquals(next.Plan, plan))
            throw new InvalidOperationException("World activation has no matching prepared scene.");
        _activating = true;
        try
        {
            Retire();
            _prepared = null;
            Current = next;
            DestinationId = plan.Request.DestinationId;
            NativeDestination = next.NativeDestination;
            try { activateBindings(next); }
            catch
            {
                try { Retire(); }
                finally { next.Dispose(); }
                throw;
            }
            return next;
        }
        finally { _activating = false; }
    }

    public void OverrideAuthoredEntry(RuntimeSpawn entry)
    {
        if (Current is not { } current) throw new InvalidOperationException("No active world has an authored entry.");
        current.OverrideAuthoredEntry(entry);
    }

    public void Retire()
    {
        CancelPreparation();
        // Bindings may still need the retiring world's facts while they detach.
        // Fusion releases its lease afterwards; a partial activation is not yet
        // in Fusion, so retire that candidate here as well.
        try { retireBindings(); }
        finally
        {
            try { releaseEntryLease(); }
            finally { Current?.Dispose(); }
        }
    }

    public void PrepareLater(Func<CancellationToken, Task> prepare, Action<Exception, bool> reportFailure)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelPreparation();
        var cancellation = new CancellationTokenSource();
        _preparation = cancellation;
        PendingPreparation = ObservePreparationAsync(prepare, reportFailure, cancellation);
    }

    public void CancelPreparation()
    {
        var cancellation = _preparation;
        _preparation = null;
        cancellation?.Cancel();
    }

    private async Task ObservePreparationAsync(Func<CancellationToken, Task> prepare,
        Action<Exception, bool> reportFailure, CancellationTokenSource cancellation)
    {
        try { await prepare(cancellation.Token); }
        catch (OperationCanceledException error) when
            (cancellation.IsCancellationRequested && error.CancellationToken == cancellation.Token)
        { }
        catch (Exception error) { reportFailure(error, cancellation.IsCancellationRequested || _disposed); }
        finally
        {
            if (ReferenceEquals(_preparation, cancellation)) _preparation = null;
            cancellation.Dispose();
        }
    }

    private void Retired(WorldSceneLease<TResources> lease)
    {
        if (ReferenceEquals(Current, lease)) Current = null;
    }

    private void DiscardPrepared()
    {
        var prepared = _prepared;
        _prepared = null;
        prepared?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelPreparation();
        try { DiscardPrepared(); }
        finally { Retire(); }
    }

    private sealed class Preparation(WorldSceneLifetime<TResources> owner, WorldSceneLease<TResources> prepared) : IDisposable
    {
        public void Dispose()
        {
            if (ReferenceEquals(owner._prepared, prepared)) owner.DiscardPrepared();
        }
    }
}

/// <summary>One scene generation; it cannot own or restore the persistent player session.</summary>
public sealed class WorldSceneLease<TResources>(WorldEntryPlan plan, ObpDestination? destination,
    FusionLabDefinition? lab, float snapReach, TResources resources,
    Action<WorldSceneLease<TResources>> retired) : IDisposable where TResources : class, IDisposable
{
    private bool _disposed;
    private readonly CancellationTokenSource _lifetime = new();
    public CancellationToken Lifetime => _lifetime.Token;
    public WorldEntryPlan Plan { get; } = plan;
    public ObpDestination? NativeDestination { get; } = destination;
    public FusionLabDefinition? Lab { get; } = lab;
    public RuntimeSpawn PlayerEntry { get; private set; } = plan.Prepared.EntryPoint;
    public float EntrySnapReach { get; } = snapReach;
    public TResources Resources { get; } = resources;
    internal void OverrideAuthoredEntry(RuntimeSpawn entry) => PlayerEntry = entry;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _lifetime.Cancel(); }
        finally
        {
            try { Resources.Dispose(); }
            finally { _lifetime.Dispose(); retired(this); }
        }
    }
}
