using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using OBP.ArchitectureTool;
using OBP.Core;
using OBP.Game.Diagnostics;

namespace OBP.Tests;

public sealed class DiagnosticScenarioTests
{
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromMinutes(2);
    private static DiagnosticScenarioDefinition Definition => new("fixture/v1", DiagnosticSourceAccess.AssetFree,
        DiagnosticSetup.AssetFreeFixture, [], TimeSpan.FromMinutes(1), 10);

    private sealed class Sources(params DiagnosticSourceSnapshot[] snapshots) : IDiagnosticSources
    {
        public ImmutableArray<DiagnosticSourceSnapshot> Snapshots { get; set; } = [.. snapshots];
        public ImmutableArray<DiagnosticSourceSnapshot> ReadSources() => Snapshots;
    }
    private sealed class Scene(DiagnosticSceneSnapshot snapshot) : IDiagnosticSceneObservations
    { public DiagnosticSceneSnapshot ReadScene() => snapshot; }
    private sealed class Clock : IDiagnosticFrameClock
    {
        public List<DiagnosticFramePhase> Phases { get; } = [];
        public Func<CancellationToken, Task>? Wait { get; set; }
        public DiagnosticFrameSnapshot ReadFrames() => new((ulong)Phases.Count(phase => phase == DiagnosticFramePhase.Process),
            (ulong)Phases.Count(phase => phase == DiagnosticFramePhase.Physics));
        public Task NextAsync(DiagnosticFramePhase phase, CancellationToken cancellationToken)
        { Phases.Add(phase); cancellationToken.ThrowIfCancellationRequested(); return Wait?.Invoke(cancellationToken) ?? Task.CompletedTask; }
    }
    private sealed class Scenario(DiagnosticScenarioDefinition definition, Func<DiagnosticRunContext, Task> execute) : IDiagnosticScenario
    {
        public DiagnosticScenarioDefinition Definition { get; } = definition;
        public Task RunAsync(DiagnosticRunContext run) => execute(run);
    }
    private sealed class Lease(Action release) : IDisposable { public void Dispose() => release(); }
    private sealed class World(Action? prepare = null) : IDiagnosticWorldSetup, IDiagnosticWorldLease
    {
        private readonly CancellationTokenSource _lifetime = new();
        public CancellationToken Lifetime => _lifetime.Token;
        public int Prepared { get; private set; }
        public int Disposed { get; private set; }
        public bool Fresh { get; private set; }
        public DiagnosticSourceAccess Access { get; private set; }
        public void Retire() => _lifetime.Cancel();
        public Task<IDiagnosticWorldLease> PrepareAsync(string destinationId, bool fresh, DiagnosticSourceAccess sourceAccess, CancellationToken cancellationToken)
        { Prepared++; Fresh = fresh; Access = sourceAccess; prepare?.Invoke(); return Task.FromResult<IDiagnosticWorldLease>(this); }
        public ValueTask DisposeAsync() { Disposed++; _lifetime.Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class ManualTime : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { var timer = new ManualTimer(callback, state); _timers.Add(timer); return timer; }
        public void Expire() { foreach (var timer in _timers.ToArray()) timer.Fire(); }
        private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool _active = true;
            public void Fire() { if (_active) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) { _active = dueTime != Timeout.InfiniteTimeSpan; return true; }
            public void Dispose() => _active = false;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    [Fact]
    public async Task ExistingStartupScenarioPreservesSixProcessFramesAndAssertionOutcome()
    {
        var clock = new Clock();
        var runner = new DiagnosticScenarioRunner(new Sources(), clock);
        var result = await runner.RunAsync(new SourceFreeStartupScenario(new Scene(new(true, true, true, true))), true, CancellationToken.None);
        Assert.Equal(DiagnosticRunStatus.Passed, result.Status); Assert.Equal(0, result.ExitCode);
        Assert.Equal(Enumerable.Repeat(DiagnosticFramePhase.Process, 6), clock.Phases);
        var failed = await runner.RunAsync(new SourceFreeStartupScenario(new Scene(new(true, true, true, false))), true, CancellationToken.None);
        Assert.Equal(DiagnosticRunStatus.Failed, failed.Status); Assert.Equal(1, failed.ExitCode);
        Assert.Equal("Asset-free startup failed to display the Game Sources UI", failed.Detail);
    }

    [Fact]
    public async Task OrdinaryLaunchCannotExecuteADiagnostic()
    {
        bool executed = false;
        var result = await new DiagnosticScenarioRunner(new Sources(), new Clock()).RunAsync(
            new Scenario(Definition, _ => { executed = true; return Task.CompletedTask; }), false, CancellationToken.None);
        Assert.False(executed); Assert.Equal(DiagnosticRunStatus.Unavailable, result.Status); Assert.NotEqual(0, result.ExitCode);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("build")]
    [InlineData("unavailable")]
    [InlineData("source")]
    public async Task SourceAdmissionRequiresTheExactAvailableAuthority(string reason)
    {
        var definition = Definition with { SourceAccess = DiagnosticSourceAccess.ExplicitSourceDiagnostic, RequiredSources = [new(ObpSourceGame.Rac2, "build")] };
        var source = new DiagnosticSourceSnapshot(reason == "source" ? ObpSourceGame.Rac3 : ObpSourceGame.Rac2,
            reason == "build" ? "replacement" : "build", reason != "unavailable");
        bool executed = false;
        var runner = new DiagnosticScenarioRunner(reason == "missing" ? new Sources() : new Sources(source), new Clock());
        var result = await runner.RunAsync(new Scenario(definition, _ => { executed = true; return Task.CompletedTask; }), true, CancellationToken.None);
        Assert.False(executed); Assert.Equal(DiagnosticRunStatus.Unavailable, result.Status); Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task OrdinaryWorldSetupStillRequiresAllThreeSources()
    {
        var definition = Definition with
        {
            SourceAccess = DiagnosticSourceAccess.OrdinaryPlay,
            Setup = DiagnosticSetup.FreshWorld,
            DestinationId = "rac1:LEVEL0",
            RequiredSources = [new(ObpSourceGame.Rac1, "r1"), new(ObpSourceGame.Rac2, "r2"), new(ObpSourceGame.Rac3, "r3")]
        };
        var world = new World();
        var missing = new DiagnosticScenarioRunner(new Sources(new(ObpSourceGame.Rac1, "r1", true), new(ObpSourceGame.Rac2, "r2", true)), new Clock(), world);
        var result = await missing.RunAsync(new Scenario(definition, _ => Task.CompletedTask), true, CancellationToken.None);
        Assert.Equal(DiagnosticRunStatus.Unavailable, result.Status); Assert.Equal(0, world.Prepared);
        var admitted = new DiagnosticScenarioRunner(new Sources(new(ObpSourceGame.Rac1, "r1", true), new(ObpSourceGame.Rac2, "r2", true), new(ObpSourceGame.Rac3, "r3", true)), new Clock(), world);
        var passed = await admitted.RunAsync(new Scenario(definition, _ => Task.CompletedTask), true, CancellationToken.None);
        Assert.Equal(DiagnosticRunStatus.Passed, passed.Status); Assert.True(world.Fresh);
        Assert.Equal(DiagnosticSourceAccess.OrdinaryPlay, world.Access); Assert.Equal(1, world.Disposed);
    }

    [Fact]
    public async Task FrameBudgetFailureRestoresEveryOwnedResourceInReverseOrder()
    {
        var released = new List<int>();
        var scenario = new Scenario(Definition with { MaximumFrameWaits = 1 }, async run =>
        {
            run.Own(new Lease(() => released.Add(1))); run.Own(new Lease(() => released.Add(2)));
            await run.WaitAsync(DiagnosticFramePhase.Physics, 2);
        });
        var result = await new DiagnosticScenarioRunner(new Sources(), new Clock()).RunAsync(scenario, true, CancellationToken.None);
        Assert.Equal(DiagnosticRunStatus.TimedOut, result.Status); Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(new[] { 2, 1 }, released);
    }

    [Fact]
    public async Task CancellationReleasesTemporaryInputAndDoesNotClaimSuccess()
    {
        using var cancel = new CancellationTokenSource();
        var state = new Dictionary<string, float> { ["move_forward"] = .25f };
        var input = new DiagnosticInputActions(state.ContainsKey, action => state[action], (action, strength) => state[action] = strength);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scenario = new Scenario(Definition, async run =>
        {
            var lease = run.Own(input.Acquire(run.CancellationToken));
            lease.SetAction("move_forward", 1); lease.SetAction("move_forward", 0); started.SetResult();
            await Task.Delay(Timeout.Infinite, run.CancellationToken);
        });
        var pending = new DiagnosticScenarioRunner(new Sources(), new Clock()).RunAsync(scenario, true, cancel.Token);
        await started.Task.WaitAsync(DeadlockGuard); cancel.Cancel();
        var result = await pending.WaitAsync(DeadlockGuard);
        Assert.Equal(.25f, state["move_forward"]); Assert.Equal(DiagnosticRunStatus.Canceled, result.Status); Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void LogicalInputValidatesActionsAndRestoresAllStatesEvenIfOneRestoreFails()
    {
        var state = new Dictionary<string, float> { ["a"] = 0, ["b"] = .5f };
        bool restoring = false;
        var input = new DiagnosticInputActions(state.ContainsKey, action => state[action], (action, strength) =>
        {
            if (restoring && action == "a") throw new IOException("input restore fixture");
            state[action] = strength;
        });
        var lease = input.Acquire(CancellationToken.None);
        Assert.Throws<ArgumentException>(() => lease.SetAction("unknown", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => lease.SetAction("a", float.NaN));
        lease.SetAction("a", 1); lease.SetAction("b", 0); restoring = true;
        Assert.Throws<AggregateException>(lease.Dispose); Assert.Equal(.5f, state["b"]);
        lease.Dispose(); Assert.Throws<ObjectDisposedException>(() => lease.SetAction("a", 0));
        Assert.Throws<OperationCanceledException>(() => input.Acquire(new CancellationToken(true)));
    }

    [Fact]
    public async Task ASourceLostDuringWorldSetupRejectsTheRunAndReleasesTheWorld()
    {
        var sources = new Sources(new DiagnosticSourceSnapshot(ObpSourceGame.Rac1, "build", true));
        var world = new World(() => sources.Snapshots = []);
        var definition = Definition with
        {
            SourceAccess = DiagnosticSourceAccess.ExplicitSourceDiagnostic,
            RequiredSources = [new(ObpSourceGame.Rac1, "build")],
            Setup = DiagnosticSetup.ExistingWorld,
            DestinationId = "rac1:LEVEL0"
        };
        bool executed = false;
        var result = await new DiagnosticScenarioRunner(sources, new Clock(), world).RunAsync(
            new Scenario(definition, _ => { executed = true; return Task.CompletedTask; }), true, CancellationToken.None);
        Assert.False(executed); Assert.Equal(DiagnosticRunStatus.Unavailable, result.Status); Assert.Equal(1, world.Disposed);
    }

    [Fact]
    public async Task FrameWaitsRetainTheRequestedSignalPhase()
    {
        var clock = new Clock();
        var scenario = new Scenario(Definition, async run =>
        {
            await run.WaitAsync(DiagnosticFramePhase.Physics);
            await run.WaitAsync(DiagnosticFramePhase.Process, 2);
            await run.WaitAsync(DiagnosticFramePhase.PostDraw);
        });
        var result = await new DiagnosticScenarioRunner(new Sources(), clock).RunAsync(scenario, true, CancellationToken.None);
        Assert.Equal(DiagnosticRunStatus.Passed, result.Status);
        Assert.Equal(new[] { DiagnosticFramePhase.Physics, DiagnosticFramePhase.Process, DiagnosticFramePhase.Process, DiagnosticFramePhase.PostDraw }, clock.Phases);
    }

    [Fact]
    public async Task DeadlineCancellationIsTestedWithoutWallClockSpeedAssumptions()
    {
        var time = new ManualTime(); bool released = false;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scenario = new Scenario(Definition, async run =>
        {
            run.Own(new Lease(() => released = true)); started.SetResult();
            await Task.Delay(Timeout.Infinite, run.CancellationToken);
        });
        var pending = new DiagnosticScenarioRunner(new Sources(), new Clock(), timeProvider: time).RunAsync(scenario, true, CancellationToken.None);
        await started.Task.WaitAsync(DeadlockGuard); time.Expire();
        var result = await pending.WaitAsync(DeadlockGuard);
        Assert.Equal(DiagnosticRunStatus.TimedOut, result.Status); Assert.NotEqual(0, result.ExitCode); Assert.True(released);
    }

    [Fact]
    public async Task RetiringTheWorldCancelsTheScenarioBeforeDisposingItsLease()
    {
        var definition = Definition with { SourceAccess = DiagnosticSourceAccess.ExplicitSourceDiagnostic, Setup = DiagnosticSetup.ExistingWorld, DestinationId = "rac1:LEVEL0" };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var world = new World();
        bool released = false;
        var scenario = new Scenario(definition, async run =>
        {
            run.Own(new Lease(() => released = true)); started.SetResult();
            await Task.Delay(Timeout.Infinite, run.CancellationToken);
        });
        var pending = new DiagnosticScenarioRunner(new Sources(), new Clock(), world).RunAsync(scenario, true, CancellationToken.None);
        await started.Task.WaitAsync(DeadlockGuard); world.Retire();
        var result = await pending.WaitAsync(DeadlockGuard);
        Assert.Equal(DiagnosticRunStatus.Canceled, result.Status); Assert.True(released); Assert.Equal(1, world.Disposed);
    }

    [Fact]
    public async Task UnexpectedExceptionsAndCleanupFailuresRemainFailures()
    {
        bool released = false;
        var scenario = new Scenario(Definition, run =>
        {
            run.Own(new Lease(() => released = true)); run.Own(new Lease(() => throw new IOException("cleanup fixture")));
            throw new InvalidOperationException("scenario fixture");
        });
        var result = await new DiagnosticScenarioRunner(new Sources(), new Clock()).RunAsync(scenario, true, CancellationToken.None);
        Assert.True(released); Assert.Equal(DiagnosticRunStatus.Failed, result.Status); Assert.NotEqual(0, result.ExitCode);
        var errors = Assert.IsType<AggregateException>(result.Error).InnerExceptions;
        Assert.Contains(errors, error => error is InvalidOperationException); Assert.Contains(errors, error => error is IOException);
    }

    [Fact]
    public async Task ForeignCancellationIsNotSuppressedAsOwnedRetirement()
    {
        using var cancel = new CancellationTokenSource();
        var scenario = new Scenario(Definition, _ => { cancel.Cancel(); throw new OperationCanceledException(CancellationToken.None); });
        var result = await new DiagnosticScenarioRunner(new Sources(), new Clock()).RunAsync(scenario, true, cancel.Token);
        Assert.Equal(DiagnosticRunStatus.Failed, result.Status); Assert.IsType<OperationCanceledException>(result.Error);
    }

    [Fact]
    public async Task UnboundCapabilitiesAreExplicitlyUnavailable()
    {
        var definition = Definition with { SourceAccess = DiagnosticSourceAccess.ExplicitSourceDiagnostic, Setup = DiagnosticSetup.FreshWorld, DestinationId = "rac2:LEVEL0" };
        var result = await new DiagnosticScenarioRunner(new Sources(), new Clock()).RunAsync(new Scenario(definition, _ => Task.CompletedTask), true, CancellationToken.None);
        Assert.Equal(DiagnosticRunStatus.Unavailable, result.Status); Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void BoundsSourcesAndSetupMetadataMustBeValid()
    {
        Assert.Throws<ArgumentException>(() => (Definition with { Timeout = TimeSpan.Zero }).Validate());
        Assert.Throws<ArgumentException>(() => (Definition with { MaximumFrameWaits = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (Definition with { RequiredSources = default }).Validate());
        Assert.Throws<ArgumentException>(() => (Definition with { SourceAccess = DiagnosticSourceAccess.OrdinaryPlay }).Validate());
        Assert.Throws<ArgumentException>(() => (Definition with { Setup = DiagnosticSetup.FreshWorld }).Validate());
        Assert.Throws<ArgumentException>(() => (Definition with { RequiredSources = [new(ObpSourceGame.Rac1, "a"), new(ObpSourceGame.Rac1, "b")] }).Validate());
    }

    [Fact]
    public void SnapshotAndActionPortsExposeNeitherGodotNodesNorTheRoot()
    {
        Type[] ports = [typeof(IDiagnosticSceneObservations), typeof(IDiagnosticWorldObservations), typeof(IDiagnosticPlayerObservations),
            typeof(IDiagnosticActorObservations), typeof(IDiagnosticEquipmentObservations), typeof(IDiagnosticAudioObservations),
            typeof(IDiagnosticReplayEvidence), typeof(IDiagnosticNavigationActions), typeof(IDiagnosticPoseObservations),
            typeof(IDiagnosticHudObservations), typeof(IDiagnosticCameraObservations), typeof(IDiagnosticUiObservations), typeof(IDiagnosticUiActions)];
        foreach (var type in ports.SelectMany(port => port.GetMethods()).SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType)))
            Assert.DoesNotContain("Godot", type.ToString(), StringComparison.Ordinal);
        var compilation = RootBoundaryAudit.Compile([CSharpSyntaxTree.ParseText("namespace OBP.Game { public class OBPGame { private int _private; } } class ExternalScenario { int Read(OBP.Game.OBPGame root) => root._private; }")]);
        Assert.Contains(compilation.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0122" && diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
