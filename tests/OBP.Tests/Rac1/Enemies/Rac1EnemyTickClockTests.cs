using OBP.Game;

namespace OBP.Tests;

public sealed class Rac1EnemyTickClockTests
{
    [Fact]
    public void NativeStepsPrecedeTheSingleProjectilePhaseAndRetainFractionalTicks()
    {
        var clock = new Rac1EnemyTickClock();
        var trace = new List<string>();
        bool Step() { trace.Add("native:" + clock.Frame); return true; }
        Action<double> Shots = _ => trace.Add("projectiles");
        clock.Advance(.5 / 60, Step, Shots);
        clock.Advance(2.5 / 60, Step, Shots);
        Assert.Equal(new[] { "projectiles", "native:1", "native:2", "native:3", "projectiles" }, trace);
    }

    [Fact]
    public void DefeatOrRetirementStopsTheRemainingNativeStepsAndProjectilePhase()
    {
        var clock = new Rac1EnemyTickClock();
        int native = 0;
        bool shots = false;
        clock.Advance(4d / 60, () => ++native < 2, _ => shots = true);
        Assert.Equal(2, native);
        Assert.Equal(2u, clock.Frame);
        Assert.False(shots);
    }

    [Fact]
    public void NewWorldResetsBothTheNativeFrameAndPendingFraction()
    {
        var clock = new Rac1EnemyTickClock();
        clock.Advance(1.5 / 60, () => true, _ => { });
        Assert.Equal(1u, clock.Frame);
        clock.Reset();
        clock.Advance(.5 / 60, () => throw new InvalidOperationException("Old world's pending fraction escaped."), _ => { });
        Assert.Equal(0u, clock.Frame);
    }

    [Fact]
    public void NegativeDeltaDoesNotRewindTheNativeClock()
    {
        var clock = new Rac1EnemyTickClock();
        double? received = null;
        clock.Advance(-1, () => throw new InvalidOperationException("No native step is due."), delta => received = delta);
        Assert.Equal(0u, clock.Frame);
        Assert.Equal(-1d, received);
    }
}
