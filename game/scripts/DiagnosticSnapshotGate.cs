namespace OBP.Game;

/// <summary>OBP diagnostic scheduling only; never a native gameplay clock.</summary>
public sealed class DiagnosticSnapshotGate
{
    private readonly double _settleSeconds;
    private double _remaining;
    public bool Pending { get; private set; }

    public DiagnosticSnapshotGate(double settleSeconds)
    {
        if (!double.IsFinite(settleSeconds) || settleSeconds < 0) throw new ArgumentOutOfRangeException(nameof(settleSeconds));
        _settleSeconds = settleSeconds;
        Reset();
    }

    public void Reset() { _remaining = _settleSeconds; Pending = true; }
    public void Request() { _remaining = 0; Pending = true; }

    public bool Tick(double delta)
    {
        if (!Pending) return false;
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        _remaining -= delta;
        if (_remaining > 0) return false;
        Pending = false;
        return true;
    }
}
