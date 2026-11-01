namespace OBP.Runtime.Gameplay;

/// <summary>
/// Host dispatch points. A NativeWorld callback owns its existing native clock;
/// dispatching it once does not synthesize a 60 Hz update or alter its delta.
/// </summary>
public enum GameplayTickPhase
{
    Prepare,
    Physics,
    Presentation,
    NativeWorld,
    Hud,
}

public sealed record GameplayTickRegistration(string Key, string Owner, GameplayTickPhase Phase, int Order);

/// <summary>
/// A single world lease's ordered, sealed callbacks. Registration belongs to the
/// activating lease; its cancellation token stops stale dispatch after retirement.
/// This class does not own any gameplay state or native simulation clock.
/// </summary>
public sealed class GameplayTickScheduler<TInput>(CancellationToken lifetime) : IDisposable
{
    private sealed record Tick(GameplayTickRegistration Metadata, Action<double> Callback, Func<bool>? Available);
    private sealed record Input(string Key, string Owner, int Order, Func<TInput, bool> Callback, Func<bool>? Available);

    private readonly List<Tick> _ticks = [];
    private readonly List<Input> _inputs = [];
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private bool _sealed;
    private bool _dispatching;
    private bool _retired;

    public IReadOnlyList<GameplayTickRegistration> SnapshotRegistrations() =>
        _ticks.Select(tick => tick.Metadata).ToArray();

    public void Register(GameplayTickPhase phase, string key, string owner, int order,
        Action<double> callback) => Register(phase, key, owner, order, callback, null);

    public void Register(GameplayTickPhase phase, string key, string owner, int order,
        Action<double> callback, Func<bool>? available)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (!Enum.IsDefined(phase)) throw new ArgumentOutOfRangeException(nameof(phase));
        CheckRegistration(key, owner);
        _ticks.Add(new(new(key, owner, phase, order), callback, available));
    }

    public void RegisterInput(string key, string owner, int order, Func<TInput, bool> callback) =>
        RegisterInput(key, owner, order, callback, null);

    public void RegisterInput(string key, string owner, int order, Func<TInput, bool> callback,
        Func<bool>? available)
    {
        ArgumentNullException.ThrowIfNull(callback);
        CheckRegistration(key, owner);
        _inputs.Add(new(key, owner, order, callback, available));
    }

    private void CheckRegistration(string key, string owner)
    {
        ObjectDisposedException.ThrowIf(_retired || lifetime.IsCancellationRequested, this);
        if (_sealed || _dispatching) throw new InvalidOperationException("World tick registration is closed.");
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (!_keys.Add(key)) throw new InvalidOperationException($"Duplicate world callback: {key}");
    }

    /// <summary>Seal once, after all source and host adapters have registered.</summary>
    public void Activate()
    {
        ObjectDisposedException.ThrowIf(_retired || lifetime.IsCancellationRequested, this);
        if (_sealed || _dispatching) throw new InvalidOperationException("World tick schedule already activated.");
        _ticks.Sort((a, b) => Compare(a.Metadata.Order, a.Metadata.Key, b.Metadata.Order, b.Metadata.Key));
        _inputs.Sort((a, b) => Compare(a.Order, a.Key, b.Order, b.Key));
        _sealed = true;
    }

    private static int Compare(int firstOrder, string firstKey, int secondOrder, string secondKey)
    {
        int order = firstOrder.CompareTo(secondOrder);
        return order != 0 ? order : StringComparer.Ordinal.Compare(firstKey, secondKey);
    }

    public void Run(GameplayTickPhase phase, double delta) => Execute(() => RunPhase(phase, delta));

    /// <summary>Preserves presentation, source-native and HUD sequencing within one process frame.</summary>
    public void RunWorldFrame(double delta) => Execute(() =>
    {
        RunPhase(GameplayTickPhase.Presentation, delta);
        RunPhase(GameplayTickPhase.NativeWorld, delta);
        RunPhase(GameplayTickPhase.Hud, delta);
    });

    private void RunPhase(GameplayTickPhase phase, double delta)
    {
        for (int i = 0; i < _ticks.Count && Active; i++)
        {
            var tick = _ticks[i];
            if (tick.Metadata.Phase != phase || tick.Available?.Invoke() == false) continue;
            tick.Callback(delta);
        }
    }

    public bool TryHandleInput(TInput input)
    {
        bool handled = false;
        Execute(() =>
        {
            for (int i = 0; i < _inputs.Count && Active; i++)
            {
                var entry = _inputs[i];
                if (entry.Available?.Invoke() != false && entry.Callback(input))
                {
                    handled = true;
                    break;
                }
            }
        });
        return handled;
    }

    private bool Active => _sealed && !_retired && !lifetime.IsCancellationRequested;

    private void Execute(Action action)
    {
        if (_retired || lifetime.IsCancellationRequested) return;
        if (!_sealed) throw new InvalidOperationException("World tick schedule is not activated.");
        if (_dispatching) throw new InvalidOperationException("Reentrant world tick or input dispatch.");
        _dispatching = true;
        try { action(); }
        finally { _dispatching = false; }
    }

    /// <summary>Immediately disables callbacks during lease retirement, including an in-flight phase.</summary>
    public void Dispose()
    {
        _retired = true;
        _ticks.Clear();
        _inputs.Clear();
        _keys.Clear();
    }
}
