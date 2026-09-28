using OBP.RAC2.Gameplay;

namespace OBP.Tests;

public sealed class GcNativeStateSessionTests
{
    [Fact]
    public void TransitionMirrorsRecoveredHelperBookkeeping()
    {
        var session = new GcNativeStateSession(
            currentState: 3,
            transitionMode: 2,
            previousState: 1,
            stateTicks: 44);

        var snapshot = session.Transition(nextState: 12, transitionMode: 4);

        Assert.Equal(12, snapshot.CurrentState);
        Assert.Equal(3, snapshot.PreviousState);
        Assert.Equal(4, snapshot.TransitionMode);
        Assert.Equal((ushort)0, snapshot.StateTicks);
    }

    [Fact]
    public void MinusOneTransitionModePreservesExistingMode()
    {
        var session = new GcNativeStateSession(12, 2);

        var snapshot = session.Transition(13);

        Assert.Equal(13, snapshot.CurrentState);
        Assert.Equal(12, snapshot.PreviousState);
        Assert.Equal(2, snapshot.TransitionMode);
        Assert.Equal((ushort)0, snapshot.StateTicks);
    }

    [Fact]
    public void StateTicksSaturateAtNativeUShortMaximum()
    {
        var session = new GcNativeStateSession(
            currentState: 3,
            transitionMode: 2,
            stateTicks: 0xFFFE);

        Assert.Equal(ushort.MaxValue, session.AdvanceTicks());
        Assert.Equal(ushort.MaxValue, session.AdvanceTicks(300));
    }
}
