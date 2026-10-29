using System.Collections.Immutable;
using System.Text.Json;
using OBP.Core;
using OBP.Game;
using OBP.Game.Diagnostics;
using OBP.Godot;

namespace OBP.Tests;

public sealed class DiagnosticRegistryTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromMinutes(1);
    private static DiagnosticScenarioDefinition Definition => new("fixture/v1", DiagnosticSourceAccess.AssetFree,
        DiagnosticSetup.AssetFreeFixture, [], TimeSpan.FromSeconds(30), 100, FailureExitCode: 3);
    private sealed class Sources : IDiagnosticSources { public ImmutableArray<DiagnosticSourceSnapshot> ReadSources() => []; }
    private sealed class Clock : IDiagnosticFrameClock
    {
        public DiagnosticFrameSnapshot ReadFrames() => new(0, 0);
        public Task NextAsync(DiagnosticFramePhase phase, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class Release(Action action) : IDisposable { public void Dispose() => action(); }

    [Fact]
    public void EveryLegacyWorldAliasSelectsExactlyItsOriginalFlagAndSourceGate()
    {
        var legacy = WorldSmokeHarness.Registry.Registrations
            .Where(registration => registration.Id != "rac3-target-seed-audit").ToArray();
        Assert.Equal(21, legacy.Length);
        Assert.Equal(22, WorldSmokeHarness.Registry.Registrations.Length);
        foreach (var registration in legacy)
        {
            var args = CommandLineArgs.Parse([Assert.Single(registration.Aliases)]);
            Assert.True(WorldSmokeHarness.IsRequested(args));
            Assert.Equal(registration, WorldSmokeHarness.Registry.Find(registration.Id));
            Assert.Equal(registration, WorldSmokeHarness.Registry.Find(registration.Aliases[0]));
            Assert.Equal(registration, Assert.Single(WorldSmokeHarness.Registry.Select(args, ObpSourceGame.Rac1, 1)));
            Assert.Empty(WorldSmokeHarness.Registry.Select(args, ObpSourceGame.Rac1, 2));
            Assert.Empty(WorldSmokeHarness.Registry.Select(args, ObpSourceGame.Rac1, 0));
            if (registration.WorldSource is not null) Assert.Empty(WorldSmokeHarness.Registry.Select(args, ObpSourceGame.Rac2, 1));
            else Assert.Single(WorldSmokeHarness.Registry.Select(args, ObpSourceGame.Rac2, 1));
        }
        Assert.Empty(WorldSmokeHarness.Registry.Select(new(), ObpSourceGame.Rac1, 1));
        Assert.False(WorldSmokeHarness.IsRequested(new()));
        Assert.True(WorldSmokeHarness.IsRequested(new() { ControllerSmoke = true }));
        Assert.True(WorldSmokeHarness.IsRequested(new() { ReplayForwardFrames = 1 }));
    }

    [Fact]
    public void MultipleExplicitFlagsKeepTheLegacySelectionOrder()
    {
        var args = new CommandLineArgs { Rac1CampaignSmoke = true, Rac1VeldinPlaySmoke = true, MovementSmoke = true };
        Assert.Equal(new[] { "rac1-veldin-play-smoke", "rac1-campaign-smoke", "movement-smoke" },
            WorldSmokeHarness.Registry.Select(args, ObpSourceGame.Rac1, 1).Select(registration => registration.Id));
    }

    [Fact]
    public void UnknownScenariosAndAmbiguousAliasesFailExplicitly()
    {
        Assert.Contains("Unknown diagnostic scenario", Assert.Throws<DiagnosticUnavailableException>(() => WorldSmokeHarness.Registry.Find("unknown")).Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new DiagnosticScenarioRegistry([
            new("one", ["alias"], _ => false), new("two", ["alias"], _ => false)]));
    }

    [Fact]
    public async Task OutcomeIsCommittedAfterScenarioAndOwnedCleanupComplete()
    {
        using var operations = new HostOperationScope();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<DiagnosticExit>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool released = false;
        var scenario = new LegacyDiagnosticScenario(Definition, async run =>
        {
            run.Own(new Release(() => released = true));
            Assert.True(ApplicationQuitDeferral.TryHandle("fixture-pass", 0));
            Assert.False(operations.Stopping);
            entered.SetResult(); await finish.Task;
        });
        DiagnosticScenarioDispatch.Start([scenario], operations, new(new Sources(), new Clock()),
            (reason, code) => { Assert.True(released); completed.SetResult(new(reason, code)); }, _ => { });
        await entered.Task.WaitAsync(Guard); Assert.False(completed.Task.IsCompleted);
        finish.SetResult(); var result = await completed.Task.WaitAsync(Guard);
        Assert.Equal(new("fixture-pass", 0), result); await operations.StopAsync();
        Assert.Empty(operations.TakeFailures()); Assert.False(ApplicationQuitDeferral.TryHandle("outside", 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AReportedFailureOrAnExceptionAfterPassCannotBecomeSuccess(bool throwAfterPass)
    {
        using var operations = new HostOperationScope();
        var completed = new TaskCompletionSource<DiagnosticExit>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scenario = new LegacyDiagnosticScenario(Definition, _ =>
        {
            ApplicationQuitDeferral.TryHandle(throwAfterPass ? "early-pass" : "fixture-fail", throwAfterPass ? 0 : 3);
            if (throwAfterPass) throw new IOException("late fixture failure");
            return Task.CompletedTask;
        });
        DiagnosticScenarioDispatch.Start([scenario], operations, new(new Sources(), new Clock()),
            (reason, code) => completed.SetResult(new(reason, code)), _ => { });
        var result = await completed.Task.WaitAsync(Guard);
        Assert.Equal(3, result.Code); if (!throwAfterPass) Assert.Equal("fixture-fail", result.Reason);
        await operations.StopAsync();
    }

    [Fact]
    public async Task MissingTerminalOutcomeIsAFailure()
    {
        var result = await new DiagnosticScenarioRunner(new Sources(), new Clock()).RunAsync(
            new LegacyDiagnosticScenario(Definition, _ => Task.CompletedTask), true, CancellationToken.None);
        Assert.Equal(DiagnosticRunStatus.Failed, result.Status); Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task InvalidMetadataReachesTheExistingCriticalHostFailureDrain()
    {
        using var operations = new HostOperationScope(); bool quit = false;
        var scenario = new LegacyDiagnosticScenario(Definition with { Timeout = TimeSpan.Zero }, _ => Task.CompletedTask);
        DiagnosticScenarioDispatch.Start([scenario], operations, new(new Sources(), new Clock()), (_, _) => quit = true, _ => { });
        await operations.StopAsync().WaitAsync(Guard);
        var failure = Assert.Single(operations.TakeFailures());
        Assert.Equal("diagnostic-scenarios", failure.Name); Assert.IsType<ArgumentException>(failure.Error);
        Assert.True(failure.RequiresFailureExit(hostStopping: true)); Assert.False(quit);
    }

    [Fact]
    public async Task LegacyCancellationRemainsOwnedWhenTheOldCallbackHandlesRetirement()
    {
        using var cancel = new CancellationTokenSource();
        var scenario = new LegacyDiagnosticScenario(Definition, _ =>
        {
            cancel.Cancel();
            var owned = new OperationCanceledException(LegacyDiagnosticCancellation.Current);
            Assert.True(LegacyDiagnosticCancellation.IsOwned(owned));
            Assert.False(LegacyDiagnosticCancellation.IsOwned(new OperationCanceledException(CancellationToken.None)));
            return Task.CompletedTask;
        });
        var result = await new DiagnosticScenarioRunner(new Sources(), new Clock()).RunAsync(scenario, true, cancel.Token);
        Assert.Equal(DiagnosticRunStatus.Canceled, result.Status);
    }

    [Fact]
    public async Task ACompletedLegacyInstanceCannotReuseItsOldPassForAnotherRun()
    {
        var scenario = new LegacyDiagnosticScenario(Definition, _ =>
        { ApplicationQuitDeferral.TryHandle("fixture-pass", 0); return Task.CompletedTask; });
        var runner = new DiagnosticScenarioRunner(new Sources(), new Clock());
        Assert.Equal(DiagnosticRunStatus.Passed, (await runner.RunAsync(scenario, true, CancellationToken.None)).Status);
        Assert.Equal(DiagnosticRunStatus.Failed, (await runner.RunAsync(scenario, true, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task CallerRetirementCancelsLegacyWaitsAndReleasesResourcesWithoutReportingAPass()
    {
        using var operations = new HostOperationScope();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool released = false, completed = false;
        var scenario = new LegacyDiagnosticScenario(Definition, async run =>
        {
            run.Own(new Release(() => released = true)); entered.SetResult();
            await Task.Delay(Timeout.Infinite, LegacyDiagnosticCancellation.Current);
        });
        DiagnosticScenarioDispatch.Start([scenario], operations, new(new Sources(), new Clock()), (_, _) => completed = true, _ => { });
        await entered.Task.WaitAsync(Guard); await operations.StopAsync().WaitAsync(Guard);
        Assert.True(released); Assert.False(completed);
    }

    [Fact]
    public async Task AnImmediateFailureCancelsAndDrainsAnAlreadyWaitingSibling()
    {
        using var operations = new HostOperationScope(); bool released = false;
        var completed = new TaskCompletionSource<DiagnosticExit>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new LegacyDiagnosticScenario(Definition, async run =>
        {
            try { await Task.Delay(Timeout.Infinite, run.CancellationToken); }
            finally { released = true; }
        });
        var failed = new LegacyDiagnosticScenario(Definition with { Id = "failed/v1" }, _ => throw new IOException("fixture failure"));
        DiagnosticScenarioDispatch.Start([pending, failed], operations, new(new Sources(), new Clock()),
            (reason, code) => { Assert.True(released); completed.SetResult(new(reason, code)); }, _ => { });
        Assert.Equal(3, (await completed.Task.WaitAsync(Guard)).Code);
        await operations.StopAsync(); Assert.Empty(operations.TakeFailures());
    }

    [Fact]
    public void TheActualInteractiveMissionLoopUsesTheCancellableWaitBoundary()
    {
        string source = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/diagnostics/Rac1MissionPlayScenario.cs"));
        Assert.Contains("while (true)", source, StringComparison.Ordinal);
        Assert.Contains("await ProcessFrameAsync()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("await ToSignal", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeferralIsIsolatedAcrossAsyncFlowsAndLateChildrenCannotCaptureAfterDisposal()
    {
        var late = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int observed = 0;
        var scope = ApplicationQuitDeferral.Begin((_, _) => observed++);
        var child = Task.Run(async () => { entered.SetResult(); await late.Task; return ApplicationQuitDeferral.TryHandle("late", 0); });
        await entered.Task.WaitAsync(Guard); scope.Dispose(); late.SetResult();
        Assert.False(await child.WaitAsync(Guard)); Assert.Equal(0, observed);
        async Task<string> Run(string id)
        {
            string? result = null;
            using var capture = ApplicationQuitDeferral.Begin((reason, _) => result = reason);
            await Task.Yield(); Assert.True(ApplicationQuitDeferral.TryHandle(id, 0)); return result!;
        }
        Assert.Equal(new[] { "a", "b" }, await Task.WhenAll(Run("a"), Run("b")));
    }

    [Fact]
    public void EvidenceRetainsItsVersionOneWireSchemaAndCheckpointOrder()
    {
        string directory = Path.Combine(Path.GetTempPath(), "obp-evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            var evidence = new AcceptanceEvidence();
            evidence.Assert(true, "first", "rac1:LEVEL0", "one");
            Assert.Throws<InvalidOperationException>(() => evidence.Assert(false, "second", "rac2:LEVEL1", "two"));
            var copy = evidence.Checks; Assert.Equal(2, copy.Length);
            string path = Path.Combine(directory, "report.json");
            evidence.Write(path, "fixture", new(true, [], null,
                [new("rac2/item/30", ObpSourceGame.Rac2, OBP.Runtime.Gameplay.EquipmentImplementation.Implemented, OBP.Runtime.Gameplay.EquipmentUsability.Ready, null)]));
            using var report = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(new[] { "Version", "Phase", "Result", "Error", "Headless", "Checks", "Sources", "Configuration", "Equipment" }, report.RootElement.EnumerateObject().Select(property => property.Name));
            Assert.Equal("failed", report.RootElement.GetProperty("Result").GetString());
            Assert.Equal("rac2", report.RootElement.GetProperty("Equipment")[0].GetProperty("SourceGame").GetString());
            Assert.Equal(new[] { "first", "second" }, report.RootElement.GetProperty("Checks").EnumerateArray().Select(check => check.GetProperty("Id").GetString()));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void EvidenceWriteOrLoggerFailureStillRequestsTheOriginalNonzeroExit()
    {
        int code = 0;
        Assert.Throws<IOException>(() => AcceptanceEvidence.FinishFailure(new InvalidOperationException("fixture"), "evidence-fail",
            () => throw new IOException("unwritable"), _ => throw new IOException("logger"), (_, exit) => code = exit));
        Assert.Equal(2, code);
    }
}
