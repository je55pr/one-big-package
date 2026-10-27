using OBP.Game.Diagnostics;

namespace OBP.Tests;

public sealed class WeaponDiagnosticInputTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task FailedOrCanceledWeaponRunReleasesInputAndEveryTransientOwner(bool canceled, bool failAudioCleanup)
    {
        using var cancellation = new CancellationTokenSource();
        var transients = new Transients { FailAudio = failAudioCleanup };
        float axis = 0;
        var input = new DiagnosticPhysicalInput((_, _) => { }, (_, _) => { }, (_, value) => axis = value);
        var definition = new DiagnosticScenarioDefinition("weapon-cleanup/v1", DiagnosticSourceAccess.AssetFree,
            DiagnosticSetup.AssetFreeFixture, [], TimeSpan.FromSeconds(10), 20);
        var inner = new Scenario(definition, async run =>
        {
            run.Own(input.Acquire()).SetAxis(DiagnosticPhysicalAxis.LeftY, -1);
            transients.Shots = transients.Audio = transients.Effects = 1;
            if (!canceled) throw new InvalidOperationException("original weapon assertion");
            cancellation.Cancel();
            await run.WaitAsync(DiagnosticFramePhase.Physics);
        });
        var result = await new DiagnosticScenarioRunner(new Sources(), new Clock()).RunAsync(
            new WeaponDiagnosticScenario(inner, transients), true, cancellation.Token);
        Assert.Equal(canceled ? DiagnosticRunStatus.Canceled : DiagnosticRunStatus.Failed, result.Status);
        Assert.Equal(0, axis);
        Assert.Equal(0, transients.Shots);
        Assert.Equal(0, transients.Audio);
        Assert.Equal(0, transients.Effects);
        Assert.Equal(1, transients.Cleanups);
        if (failAudioCleanup)
        {
            var errors = Assert.IsType<AggregateException>(result.Error).Flatten().InnerExceptions;
            Assert.Contains(errors, error => error.Message == "original weapon assertion");
            Assert.Contains(errors, error => error.Message == "audio cleanup failed");
        }
    }

    [Fact]
    public void StickOwnersDisposedOutOfOrderPreserveTheOtherSimulation()
    {
        float current = 0; var changes = new List<float>();
        var input = new DiagnosticPhysicalInput((_, _) => { }, (_, _) => { }, (_, value) => { current = value; changes.Add(value); });
        using var first = input.Acquire(); using var second = input.Acquire();
        first.SetAxis(DiagnosticPhysicalAxis.LeftY, -.5f); second.SetAxis(DiagnosticPhysicalAxis.LeftY, -1);
        first.Dispose(); Assert.Equal(-1, current);
        second.Dispose(); Assert.Equal(0, current);
        Assert.Equal(new[] { -.5f, -1, 0 }, changes);
    }
    [Fact]
    public void FailedStickReleaseCanBeRetriedWithoutHoldingADeadOwner()
    {
        float current = 0; bool failRelease = true;
        var input = new DiagnosticPhysicalInput((_, _) => { }, (_, _) => { }, (_, value) =>
        {
            if (value == 0 && failRelease) { failRelease = false; throw new IOException("expected partial release"); }
            current = value;
        });
        using var lease = input.Acquire(); lease.SetAxis(DiagnosticPhysicalAxis.LeftX, 1);
        Assert.Throws<AggregateException>(lease.Dispose); Assert.Equal(1, current);
        lease.Dispose(); Assert.Equal(0, current);
    }

    private sealed class Release(Action release) : IDisposable { public void Dispose() => release(); }
    private sealed class Transients : IWeaponDiagnosticTransientFixture
    {
        public int Shots, Audio, Effects, Cleanups;
        public bool FailAudio;
        public IDisposable BeginCleanup() => new Release(() =>
        {
            Cleanups++;
            DiagnosticCleanup.Run("weapon fixture cleanup", () => Shots = 0,
                () => { Audio = 0; if (FailAudio) throw new IOException("audio cleanup failed"); }, () => Effects = 0);
        });
    }
    private sealed class Scenario(DiagnosticScenarioDefinition definition, Func<DiagnosticRunContext, Task> run) : IDiagnosticScenario
    {
        public DiagnosticScenarioDefinition Definition => definition;
        public Task RunAsync(DiagnosticRunContext context) => run(context);
    }
    private sealed class Sources : IDiagnosticSources
    {
        public System.Collections.Immutable.ImmutableArray<DiagnosticSourceSnapshot> ReadSources() => [];
    }
    private sealed class Clock : IDiagnosticFrameClock
    {
        public DiagnosticFrameSnapshot ReadFrames() => new(0, 0);
        public Task NextAsync(DiagnosticFramePhase phase, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    }
}
