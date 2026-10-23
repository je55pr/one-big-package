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
    private double _retrySecondsRemaining;
    private int _consecutiveWriteFailures;

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
        if (!double.IsFinite(seconds) || seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds));

        bool queued = false;
        if (_dirty)
        {
            _quiet += seconds;
            _age += seconds;
            if (_quiet >= QuietSeconds || _age >= MaximumDelaySeconds)
            {
                // Snapshot mutable gameplay state on the main thread.
                Rac1CampaignSaveEnvelope snapshot = capture();
                _dirty = false;
                _quiet = 0;
                _age = 0;
                lock (_gate)
                {
                    // A newer snapshot supersedes a failed or waiting write.
                    _pending = snapshot;
                }
                queued = true;
            }
        }

        lock (_gate)
        {
            // Idle frames must retry after an IO failure even when _dirty is
            // false and no further pickups or gameplay mutations occur.
            if (!_draining && _pending is not null && _failure is not null)
                _retrySecondsRemaining = Math.Max(0, _retrySecondsRemaining - seconds);

            if (!_draining && _pending is not null && _retrySecondsRemaining <= 0)
            {
                _draining = true;
                _worker = Task.Run(Drain);
                return true;
            }
        }
        return queued;
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
                lock (_gate)
                {
                    _failure = null;
                    _consecutiveWriteFailures = 0;
                    _retrySecondsRemaining = 0;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or ArgumentException or InvalidOperationException)
            {
                lock (_gate)
                {
                    _pending ??= snapshot;
                    _failure = ex;
                    _consecutiveWriteFailures++;
                    // Exponential backoff, capped to avoid busy-looping on a
                    // broken volume. Main-thread Tick schedules the retry.
                    _retrySecondsRemaining = Math.Min(5, .5 * Math.Pow(2, Math.Min(_consecutiveWriteFailures - 1, 5)));
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
            lock (_gate)
            {
                // Background failure may have requeued an older snapshot
                // while we waited. The just-flushed authoritative snapshot
                // supersedes it; do not let a stale retry overwrite it.
                _pending = null;
                _failure = null;
                _consecutiveWriteFailures = 0;
                _retrySecondsRemaining = 0;
            }
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
