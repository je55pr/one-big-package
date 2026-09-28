namespace OBP.RAC2.Gameplay;

/// <summary>
/// Recovered GC Moby native-state transition bookkeeping.
/// Retail helper 0x31A658 writes the new state at Moby +0x20, preserves the
/// old state at +0x94, resets +0x96, and optionally replaces +0x95.
/// </summary>
public sealed class GcNativeStateSession
{
    public GcNativeStateSession(
        int currentState,
        int transitionMode,
        int previousState = 0,
        ushort stateTicks = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentState);
        ArgumentOutOfRangeException.ThrowIfNegative(transitionMode);
        ArgumentOutOfRangeException.ThrowIfNegative(previousState);
        CurrentState = currentState;
        TransitionMode = transitionMode;
        PreviousState = previousState;
        StateTicks = stateTicks;
    }

    public int CurrentState { get; private set; }
    public int PreviousState { get; private set; }
    public int TransitionMode { get; private set; }
    public ushort StateTicks { get; private set; }

    /// <summary>
    /// Mirrors helper 0x31A658. A transition-mode value of -1 means preserve
    /// the existing mode.
    /// </summary>
    public GcNativeStateSnapshot Transition(int nextState, int transitionMode = -1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nextState);
        if (transitionMode < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(transitionMode));
        }

        PreviousState = CurrentState;
        CurrentState = nextState;
        StateTicks = 0;
        if (transitionMode != -1)
        {
            TransitionMode = transitionMode;
        }

        return Snapshot();
    }

    /// <summary>
    /// Retail increments Moby +0x96 until it reaches 0xFFFF.
    /// </summary>
    public ushort AdvanceTicks(int ticks = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        uint advanced = (uint)StateTicks + (uint)ticks;
        StateTicks = (ushort)Math.Min(advanced, ushort.MaxValue);
        return StateTicks;
    }

    public GcNativeStateSnapshot Snapshot() =>
        new(CurrentState, PreviousState, TransitionMode, StateTicks);
}

public sealed record GcNativeStateSnapshot(
    int CurrentState,
    int PreviousState,
    int TransitionMode,
    ushort StateTicks);
