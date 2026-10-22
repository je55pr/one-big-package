namespace OBP.RAC1.Progression;

/// <summary>
/// OBP host policy: coalesce rapid state mutations and serialize immutable
/// snapshots in a single background writer. Durable boundaries flush in order.
/// </summary>
public sealed class Rac1CampaignSaveBatcher
{
    public const double QuietSeconds = 0.75;
    public const double MaximumDelaySeconds = 3.0;

    private readonly Action<Rac1CampaignSaveEnvelope> _write;
    private readonly object _gate = new();
    private Rac1CampaignSaveEnvelope? _pending;
    private Task _worker = Task.CompletedTask;
    private bool _draining;
    private Exception? _failure;
    private bool _dirty;
    private double _quiet;
    private double _age;

    public Rac1CampaignSaveBatcher(Action<Rac1CampaignSaveEnvelope> write) =>
        _write = write ?? throw new ArgumentNullException(nameof(write));

    public bool Dirty => _dirty;
    public Exception? Failure
    {
        get { lock (_gate) return _failure; }
    }

    public void MarkDirty()
    {
        if (!_dirty) _age = 0;
        _quiet = 0;
        _dirty = true;
    }

    /// <summary>Called by the main thread; capture must snapshot mutable state.</summary>
    public bool Tick(double seconds, Func<Rac1CampaignSaveEnvelope> capture)
    {
        if (!_dirty) return false;
        if (!double.IsFinite(seconds) || seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds));

        _quiet += seconds;
        _age += seconds;
        if (_quiet < QuietSeconds && _age < MaximumDelaySeconds) return false;

        Rac1CampaignSaveEnvelope snapshot = capture();
        _dirty = false;
        _quiet = 0;
        _age = 0;
        lock (_gate)
        {
            // Only the latest not-yet-started snapshot needs to reach disk.
            _pending = snapshot;
            if (!_draining)
            {
                _draining = true;
                _worker = Task.Run(Drain);
            }
        }
        return true;
    }

    private void Drain()
    {
        while (true)
        {
            Rac1CampaignSaveEnvelope snapshot;
            lock (_gate)
            {
                if (_pending is null)
                {
                    _draining = false;
                    return;
                }
                snapshot = _pending;
                _pending = null;
            }

            try
            {
                _write(snapshot);
                lock (_gate) _failure = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or ArgumentException or InvalidOperationException)
            {
                lock (_gate)
                {
                    _pending ??= snapshot;
                    _failure = ex;
                    _draining = false;
                }
                return;
            }
        }
    }

    /// <summary>
    /// Level travel and clean exit are durability boundaries, not gameplay frames.
    /// Even if a background attempt failed, retry the latest authoritative state.
    /// </summary>
    public bool FlushNow(Func<Rac1CampaignSaveEnvelope> capture, out Exception? error)
    {
        Task running;
        bool hasPending;
        lock (_gate)
        {
            hasPending = _dirty || _pending is not null || _draining || _failure is not null;
            _pending = null;
            running = _worker;
        }
        if (!hasPending)
        {
            error = null;
            return true;
        }

        try
        {
            running.GetAwaiter().GetResult();
            _write(capture());
            lock (_gate) _failure = null;
            _dirty = false;
            _quiet = 0;
            _age = 0;
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException)
        {
            lock (_gate) _failure = ex;
            _dirty = true;
            error = ex;
            return false;
        }
    }
}
