using OneBigPackage;

namespace OBP.Tests;

public sealed class DiagnosticSnapshotGateTests
{
    [Fact]
    public void EntryDelayAndCompletedDiagnosticsExecuteNoSelectionQueries()
    {
        var gate = new DiagnosticSnapshotGate(0.75);
        int calls = 0;
        void Tick(double delta) { if (gate.Tick(delta)) calls += 62 * 2; }
        Tick(0.25); Tick(0.25);
        Assert.Equal(0, calls);
        Tick(0.25);
        Assert.Equal(124, calls);
        Assert.False(gate.Pending);
        for (int frame = 0; frame < 600; frame++) Tick(1d / 60);
        Assert.Equal(124, calls); // Previously 74,772 queries across these 603 ticks.
    }

    [Fact]
    public void ExplicitRequestsSampleTheCurrentStateAndWorldResetStartsANewDelay()
    {
        var gate = new DiagnosticSnapshotGate(0.75);
        int ratchetPosition = 1, snapshot = 0, gameplayTicks = 0;
        void Tick(double delta)
        {
            gameplayTicks++;
            if (gate.Tick(delta)) snapshot = ratchetPosition;
        }
        Tick(0.75);
        Assert.Equal(1, snapshot);
        ratchetPosition = 2;
        Tick(1);
        Assert.Equal(1, snapshot);
        gate.Request(); Tick(0);
        Assert.Equal(2, snapshot);
        gate.Reset(); ratchetPosition = 3;
        Tick(0.5); Assert.Equal(2, snapshot);
        Tick(0.25); Assert.Equal(3, snapshot);
        Assert.Equal(5, gameplayTicks);
    }
}
