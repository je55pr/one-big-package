using OBP.Game.Diagnostics;

namespace OBP.Tests;

public sealed class WeaponDiagnosticInputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedReloadCannotAcquireCleanupOrReportSuccess(bool canceled)
    {
        using var lifetime = new CancellationTokenSource(); var transients = new Transients();
        Task<IDisposable> Reload() => WeaponDiagnosticReload.LoadAsync(_ =>
        {
            if (canceled) lifetime.Cancel();
            return Task.FromResult(false);
        }, transients, lifetime.Token);
        if (canceled)
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(Reload);
            Assert.Equal(lifetime.Token, error.CancellationToken);
        }
        else await Assert.ThrowsAsync<DiagnosticUnavailableException>(Reload);
        Assert.Equal(0, transients.Acquisitions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterCommittedReloadCleansTheNewWorld(bool failAudioCleanup)
    {
        using var lifetime = new CancellationTokenSource();
        var transients = new Transients { FailAudio = failAudioCleanup, Shots = 1, Audio = 1, Effects = 1 };
        Task<IDisposable> Reload() => WeaponDiagnosticReload.LoadAsync(_ =>
        {
            lifetime.Cancel(); return Task.FromResult(true);
        }, transients, lifetime.Token);
        if (failAudioCleanup)
        {
            var error = await Assert.ThrowsAsync<AggregateException>(Reload);
            Assert.Contains(error.InnerExceptions, inner => inner is OperationCanceledException canceled && canceled.CancellationToken == lifetime.Token);
            Assert.Contains(error.Flatten().InnerExceptions, inner => inner is IOException);
        }
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(Reload);
        Assert.Equal(1, transients.Acquisitions); Assert.Equal(1, transients.Cleanups);
        Assert.Equal(0, transients.Shots); Assert.Equal(0, transients.Audio); Assert.Equal(0, transients.Effects);
    }

    [Fact]
    public async Task ConfirmedReloadTransfersItsCleanupLeaseToTheCaller()
    {
        var transients = new Transients { Shots = 1, Audio = 1, Effects = 1 };
        var cleanup = await WeaponDiagnosticReload.LoadAsync(_ => Task.FromResult(true), transients, CancellationToken.None);
        Assert.Equal(1, transients.Acquisitions); Assert.Equal(0, transients.Cleanups);
        cleanup.Dispose(); Assert.Equal(1, transients.Cleanups);
        Assert.Equal(0, transients.Shots); Assert.Equal(0, transients.Audio); Assert.Equal(0, transients.Effects);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
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
        Assert.Equal(canceled && !failAudioCleanup ? DiagnosticRunStatus.Canceled : DiagnosticRunStatus.Failed, result.Status);
        Assert.Equal(0, axis);
        Assert.Equal(0, transients.Shots);
        Assert.Equal(0, transients.Audio);
        Assert.Equal(0, transients.Effects);
        Assert.Equal(1, transients.Cleanups);
        if (failAudioCleanup)
        {
            var errors = Assert.IsType<AggregateException>(result.Error).Flatten().InnerExceptions;
            if (!canceled) Assert.Contains(errors, error => error.Message == "original weapon assertion");
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

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(1.01f)]
    [InlineData(-1.01f)]
    public void InvalidStickStrengthCannotPoisonInputOwnership(float value)
    {
        int writes = 0;
        var input = new DiagnosticPhysicalInput((_, _) => { }, (_, _) => { }, (_, _) => writes++);
        using var lease = input.Acquire();
        Assert.Throws<ArgumentOutOfRangeException>(() => lease.SetAxis(DiagnosticPhysicalAxis.LeftX, value));
        lease.Dispose(); Assert.Equal(0, writes);
    }

    [Fact]
    public void UndefinedAxisAndDisposedLeaseCannotWriteInput()
    {
        int writes = 0;
        var input = new DiagnosticPhysicalInput((_, _) => { }, (_, _) => { }, (_, _) => writes++);
        using var lease = input.Acquire();
        Assert.Throws<ArgumentOutOfRangeException>(() => lease.SetAxis((DiagnosticPhysicalAxis)42, 1));
        lease.Dispose();
        Assert.Throws<ObjectDisposedException>(() => lease.SetAxis(DiagnosticPhysicalAxis.LeftY, 1));
        Assert.Equal(0, writes);
    }

    [Fact]
    public void PartiallyAppliedAxisWriteCanBeRetriedAndReleased()
    {
        float current = 0; bool fail = true;
        var input = new DiagnosticPhysicalInput((_, _) => { }, (_, _) => { }, (_, value) =>
        {
            current = value;
            if (fail) { fail = false; throw new IOException("partially applied axis"); }
        });
        using var lease = input.Acquire();
        Assert.Throws<IOException>(() => lease.SetAxis(DiagnosticPhysicalAxis.LeftX, 1)); Assert.Equal(1, current);
        lease.SetAxis(DiagnosticPhysicalAxis.LeftX, 1); Assert.Equal(1, current);
        lease.Dispose(); Assert.Equal(0, current);
    }

    private sealed class Release(Action release) : IDisposable { public void Dispose() => release(); }
    private sealed class Transients : IWeaponDiagnosticTransientFixture
    {
        public int Shots, Audio, Effects, Acquisitions, Cleanups;
        public bool FailAudio;
        public IDisposable BeginCleanup()
        {
            Acquisitions++;
            return new Release(() =>
            {
                Cleanups++;
                DiagnosticCleanup.Run("weapon fixture cleanup", () => Shots = 0,
                    () => { Audio = 0; if (FailAudio) throw new IOException("audio cleanup failed"); }, () => Effects = 0);
            });
        }
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
