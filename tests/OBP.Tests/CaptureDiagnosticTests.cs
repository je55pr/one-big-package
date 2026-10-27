using System.Collections.Immutable;
using System.Numerics;
using OBP.Game.Diagnostics;
using OBP.Game;
using OBP.Core;

namespace OBP.Tests;

public sealed class CaptureDiagnosticTests
{
    [Fact]
    public void AcceptanceWithoutAnOutputDoesNotReadUnavailablePresentationState()
    {
        var reporter = new OwnedDiagnosticAcceptanceReporter(() => "fixture", () => null,
            () => throw new InvalidOperationException("No presentation snapshot is needed."), _ => { }, (_, _) => { });
        reporter.Assert(true, "fixture", "ordered assertion");
        reporter.Write("capture-route");
    }

    [Fact]
    public void FailedAcceptanceStillQuitsWithFailureWhenEvidenceCannotBeCaptured()
    {
        var messages = new List<string>();
        (string Reason, int Code)? stopped = null;
        var reporter = new OwnedDiagnosticAcceptanceReporter(() => "fixture", () => "unused-fixture-output",
            () => throw new IOException("fixture snapshot failed"), messages.Add, (reason, code) => stopped = (reason, code));
        reporter.Fail("capture-route", new InvalidOperationException("original failure"), "capture-failed");
        Assert.Equal(("capture-failed", 2), stopped);
        Assert.Contains(messages, message => message.Contains("fixture snapshot failed", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Contains("original failure", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, "key F9 True Physical")]
    [InlineData(1, "pad Back True")]
    public async Task OrdinaryRecordingRetainsTheOriginalSignalAndInputProtocol(int level, string terminal)
    {
        var fixture = new Fixture { State = Fixture.Initial with { LevelId = level } };
        using var run = new DiagnosticRunContext(fixture, CancellationToken.None, 2000);
        await new PlaytestRecordingScenario(fixture.Context(run), fixture).RunAsync();
        Assert.Equal(499UL, fixture.Physics);
        Assert.Equal(28UL, fixture.Process);
        Assert.Equal(new[]
        {
            "30/0 axis LeftY -0.55", "70/0 axis LeftY 0", "85/0 key Space True Both", "95/0 key Space False Both",
            "170/0 axis RightX 0.65", "205/0 axis RightX 0", "205/0 key E True Both", "208/0 key E False Both",
            "233/0 key E True Both", "236/0 key E False Both", "386/0 pad X True", "389/0 pad X False",
            "464/0 pad Y True", "484/0 pad Y False", "489/0 resize 1000 700", "499/0 pad Start True",
            "499/1 pad Start False", "499/16 pad Down True", "499/18 pad Down False",
            "499/23 key F8 True Logical", "499/24 key F8 False Logical", "499/25 key F8 True Physical",
            "499/26 key F8 False Physical", "499/27 pad X True", "499/28 pad X False", "499/28 " + terminal,
        }, fixture.Protocol);
        Assert.False(fixture.Saved); // The ordinary writer observes the terminal input itself.
    }

    [Fact]
    public async Task FiveStopCaptureRetainsQualifiedEquipmentBookmarksAndThePersistentPlayer()
    {
        var fixture = new Fixture();
        using var run = new DiagnosticRunContext(fixture, CancellationToken.None, 5000);
        await new ReconciliationCaptureScenario(fixture.Context(run), fixture, fixture, fixture, fixture).RunAsync();
        Assert.Equal(new[] { "rac1:LEVEL0", "rac2:LEVEL1", "rac3:TABLE1", "obp:fusion-lab", "rac1:LEVEL0" }, ReconciliationCaptureScenario.Route);
        Assert.Equal(ReconciliationCaptureScenario.Route.Skip(1), fixture.Visits);
        Assert.Equal(ReconciliationCaptureScenario.Route.Select((world, index) => (index, world)), fixture.Bookmarks);
        Assert.Equal(Enumerable.Repeat("7:rac3/item/43", 5), fixture.Slots);
        Assert.Equal(Enumerable.Range(0, 5).SelectMany(_ => new[] { "rac2/item/30", "rac3/item/43" }), fixture.Selections);
        Assert.Equal(795UL, fixture.Physics);
        Assert.Equal(5UL, fixture.Process);
        Assert.Equal(29, fixture.Checks.Count);
        Assert.Equal(new[] { "capture-route" }, fixture.Writes);
        Assert.True(fixture.Saved);
        Assert.Equal(10, fixture.Protocol.Count(item => item.Contains("key F8", StringComparison.Ordinal)));
        Assert.All(fixture.Protocol.Where(item => item.Contains("key F8", StringComparison.Ordinal)), item => Assert.EndsWith("Physical", item));
    }

    [Theory]
    [InlineData(true, false, "transition-preserves-session")]
    [InlineData(false, true, "universal-bookmark")]
    public async Task CaptureCannotSaveWhenTheSessionChangesOrABookmarkNeverCompletes(bool mutate, bool missing, string check)
    {
        var fixture = new Fixture { MutateProfile = mutate, MissingBookmark = missing };
        using var run = new DiagnosticRunContext(fixture, CancellationToken.None, 5000);
        var reporter = new OwnedDiagnosticAcceptanceReporter(() => fixture.State.Destination, () => null,
            () => throw new InvalidOperationException("No output snapshot requested."), _ => { }, (_, _) => { });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ReconciliationCaptureScenario(fixture.Context(run), fixture, fixture, fixture, reporter).RunAsync());
        Assert.Contains(check, error.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Writes);
        Assert.False(fixture.Saved);
        if (missing) Assert.Equal(601UL, fixture.Process);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public async Task FinishedCaptureProofRequiresFinishedRecorderAndRunningPersistentClock(bool finished, bool preparing, bool fail)
    {
        var fixture = new Fixture { State = Fixture.Initial with { Finished = finished, PreparingWorld = preparing } };
        using var run = new DiagnosticRunContext(fixture, CancellationToken.None, 100);
        Task verify() => new ReconciliationCaptureScenario(fixture.Context(run), fixture, fixture, fixture, fixture).VerifyFinishedContinuationAsync();
        if (fail) { await Assert.ThrowsAsync<InvalidOperationException>(verify); Assert.Empty(fixture.Writes); }
        else { await verify(); Assert.Equal(new[] { "capture-route" }, fixture.Writes); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureOwnsAndDisposesItsInputLeaseOnFailureOrCancellation(bool cancel)
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var definition = new DiagnosticScenarioDefinition("capture-fixture/v1", DiagnosticSourceAccess.AssetFree,
            DiagnosticSetup.AssetFreeFixture, [], TimeSpan.FromSeconds(5), 100);
        var scenario = new CaptureOwnedScenario(definition, fixture, fixture, _ => { }, context =>
        {
            context.Input.Key(CaptureKey.E, true);
            if (cancel) { cancellation.Cancel(); context.Run.CancellationToken.ThrowIfCancellationRequested(); }
            throw new InvalidOperationException("fixture failure");
        });
        var result = await new DiagnosticScenarioRunner(fixture, fixture).RunAsync(scenario, true, cancellation.Token);
        Assert.Equal(cancel ? DiagnosticRunStatus.Canceled : DiagnosticRunStatus.Failed, result.Status);
        Assert.True(fixture.Disposed);
    }

    [Fact]
    public async Task RecordingCannotSendInputBeforeEntryCompletes()
    {
        var fixture = new Fixture { State = Fixture.Initial with { WaitingForEntry = true } };
        using var run = new DiagnosticRunContext(fixture, CancellationToken.None, 400);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PlaytestRecordingScenario(fixture.Context(run), fixture).RunAsync());
        Assert.Equal(360UL, fixture.Process);
        Assert.Empty(fixture.Protocol);
    }

    [Theory]
    [InlineData(25, 0, false)]
    [InlineData(25, 25, false)]
    [InlineData(25, 26, true)]
    [InlineData(25, 27, false)]
    [InlineData(15, 0, false)]
    [InlineData(15, 15, false)]
    [InlineData(15, 16, true)]
    public void MovementReplayComparisonsRequireTheWholeRequestedTrace(int frames, int count, bool accepted)
    {
        var samples = Enumerable.Repeat(new MovementStateReplayDiagnostic(new(0, 0, 0), 0), count).ToImmutableArray();
        if (accepted) Assert.Equal(samples, MovementStateScenario.RequireReplaySamples(samples, frames, "fixture route"));
        else Assert.Contains("fixture route", Assert.Throws<InvalidOperationException>(() => MovementStateScenario.RequireReplaySamples(samples, frames, "fixture route")).Message);
    }

    [Fact]
    public void IsolationUsesDirectoryBoundariesAndNormalizesEscapes()
    {
        string captures = Path.Combine(Path.GetPathRoot(RepoPaths.Root)!, "obp-portable-path-fixture", "captures");
        Assert.True(CaptureDiagnosticIsolation.Isolated(null, captures));
        Assert.True(CaptureDiagnosticIsolation.Isolated(Path.Combine(captures, "profile.json"), captures));
        Assert.True(CaptureDiagnosticIsolation.Isolated(Path.Combine(Path.GetTempPath(), "profile.json"), captures));
        Assert.False(CaptureDiagnosticIsolation.Isolated(Path.Combine(captures + "-evil", "profile.json"), captures));
        Assert.False(CaptureDiagnosticIsolation.Isolated(Path.Combine(captures, "..", "escape", "profile.json"), captures));
    }

    [Fact]
    public async Task NonTerminalSupervisionWaitsForWriterCommitBeforeReleasingInputAndNeverQuits()
    {
        var fixture = new Fixture { HoldFrames = true };
        using var operations = new HostOperationScope();
        var harness = fixture.Harness(operations);
        harness.Start("capture-fixture", _ => Task.CompletedTask, "capture-failed", 7, false);
        Assert.Equal(1, operations.PendingCount);
        Assert.False(fixture.Disposed);
        Assert.Empty(fixture.Quits);
        fixture.State = fixture.State with { Finished = true };
        fixture.NextFrame.TrySetResult();
        await fixture.Released.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await operations.StopAsync();
        Assert.True(fixture.Disposed);
        Assert.Empty(fixture.Quits);
        Assert.Empty(operations.TakeFailures());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SupervisionKeepsFailureReasonCodeAndOptionalAcceptanceEvidence(bool evidence)
    {
        var fixture = new Fixture();
        using var operations = new HostOperationScope();
        fixture.Harness(operations).Start("capture-fixture", _ => throw new InvalidDataException("original fixture failure"),
            "capture-failed", 7, false, evidence ? "capture-route" : null);
        Assert.Equal(new[] { ("capture-failed", evidence ? 2 : 7) }, fixture.Quits);
        Assert.True(fixture.Disposed);
        if (evidence)
        {
            var failure = Assert.Single(fixture.Failures);
            Assert.Equal("capture-route", failure.Phase);
            Assert.Equal("original fixture failure", failure.Error.Message);
        }
        else Assert.Contains(fixture.Errors, message => message.Contains("original fixture failure", StringComparison.Ordinal));
    }

    [Fact]
    public void TerminalPassQuitsOnlyAfterInputCleanupAndRequirementsRefreshOnStart()
    {
        var fixture = new Fixture();
        using var operations = new HostOperationScope();
        fixture.Sources = [new(ObpSourceGame.Rac1, "fixture", true), new(ObpSourceGame.Rac2, "fixture", true)];
        var harness = fixture.Harness(operations);
        fixture.Sources = [new(ObpSourceGame.Rac1, "fixture", true)];
        harness.Start("capture-fixture", _ => Task.CompletedTask, "capture-failed", 7, true);
        Assert.Equal(new[] { ("capture-fixture-pass", 0) }, fixture.Quits);
        Assert.True(fixture.Disposed);
        Assert.True(fixture.DisposedAtQuit);
    }

    [Fact]
    public void MultiSourceRoutesRejectPartialAdmissionBeforeExecuting()
    {
        var fixture = new Fixture();
        using var operations = new HostOperationScope();
        bool executed = false;
        fixture.Harness(operations).Start("trilogy-gameplay-smoke", _ => { executed = true; return Task.CompletedTask; }, "missing-source", 1, true);
        Assert.False(executed);
        Assert.Equal(new[] { ("missing-source", 1) }, fixture.Quits);
    }

    [Fact]
    public async Task HostCancellationReleasesInputWithoutClaimingAResult()
    {
        var fixture = new Fixture { HoldFrames = true };
        using var operations = new HostOperationScope();
        fixture.Harness(operations).Start("capture-fixture", context => context.Run.WaitAsync(DiagnosticFramePhase.Process), "capture-failed", 7, true);
        await operations.StopAsync();
        Assert.True(fixture.Disposed);
        Assert.Empty(fixture.Quits);
        Assert.Empty(fixture.Failures);
        Assert.Empty(operations.TakeFailures());
    }

    [Fact]
    public async Task FinishedContinuationPreservesOwnedCancellationRatherThanAFailedSave()
    {
        var fixture = new Fixture { HoldFrames = true, Sources = Fixture.AllSources };
        using var operations = new HostOperationScope();
        var task = fixture.Harness(operations).VerifyFinishedContinuationAsync();
        await operations.StopAsync();
        var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(operations.Token, canceled.CancellationToken);
        Assert.Empty(fixture.Quits);
        Assert.Empty(fixture.Failures);
    }

    [Fact]
    public async Task RetiredMovementDiagnosticReachesRunnerAdmissionAndReturnsCanceledWithoutAcquiringInput()
    {
        var fixture = new Fixture();
        using var operations = new HostOperationScope();
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.Equal(DiagnosticRunStatus.Canceled, await fixture.Harness(operations).CheckMovementCancellationAsync(canceled.Token));
        Assert.False(fixture.Disposed); // Admission rejects before a scenario input lease exists.
        Assert.Empty(fixture.Quits);
        Assert.Empty(fixture.Failures);
        Assert.False(operations.Stopping);
    }

    [Fact]
    public void UnpacedReportPollingRetainsItsWallDeadlineRatherThanA60HzFrameBudget()
    {
        var fixture = new Fixture();
        using var operations = new HostOperationScope();
        fixture.Harness(operations).Start("bug-report-smoke", context => context.Run.WaitAsync(DiagnosticFramePhase.Process, 108_001), "report-failed", 1, true);
        Assert.Equal(108_001UL, fixture.Process);
        Assert.Equal(new[] { ("bug-report-smoke-pass", 0) }, fixture.Quits);
    }

    [Fact]
    public void SupervisionRetainsOriginalFailureAlongsideInputCleanupFailure()
    {
        var fixture = new Fixture { FailDispose = true };
        using var operations = new HostOperationScope();
        fixture.Harness(operations).Start("capture-fixture", _ => throw new InvalidDataException("original fixture failure"), "capture-failed", 7, true, "capture-route");
        var failure = Assert.IsType<AggregateException>(Assert.Single(fixture.Failures).Error);
        Assert.Contains(failure.InnerExceptions, error => error.Message == "original fixture failure");
        Assert.Contains(failure.InnerExceptions, error => error.Message == "fixture cleanup failure");
        Assert.Equal(new[] { ("capture-failed", 2) }, fixture.Quits);
    }

    private sealed class Fixture : ICaptureRecorderDiagnosticFixture, ICaptureDiagnosticInput, ICaptureDiagnosticInputFactory,
        ICaptureDiagnosticTime, IDiagnosticFrameClock, IDiagnosticSources, IDiagnosticNavigationActions, ICaptureEquipmentActions, IDiagnosticAcceptanceReporter
    {
        internal static CaptureRuntimeDiagnostic Initial => new()
        {
            RecorderPresent = true,
            PlayerPresent = true,
            Destination = "rac1:LEVEL0",
            LevelId = 0,
            Configuration = PlayerSessionLifetimeTests.Snapshot().Configuration,
            ProfileJson = "fixture-player",
            GenuineAvatar = true,
            NativeProfile = true,
            PhysicsTicksPerSecond = 60,
            HudHealth = true,
            HudBolts = true,
            HudVisible = true,
            PlayerNode = 17,
        };
        internal CaptureRuntimeDiagnostic State { get; set; } = Initial;
        internal ulong Physics, Process;
        internal bool Saved, MutateProfile, MissingBookmark, Disposed, HoldFrames, DisposedAtQuit, FailDispose;
        internal static ImmutableArray<DiagnosticSourceSnapshot> AllSources => [new(ObpSourceGame.Rac1, "fixture", true), new(ObpSourceGame.Rac2, "fixture", true), new(ObpSourceGame.Rac3, "fixture", true)];
        internal ImmutableArray<DiagnosticSourceSnapshot> Sources = [new(ObpSourceGame.Rac1, "fixture", true)];
        internal TaskCompletionSource NextFrame { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<(string, int)> Quits { get; } = [];
        internal List<string> Errors { get; } = [];
        internal List<(string Phase, Exception Error, string Reason)> Failures { get; } = [];
        internal CaptureDiagnosticHarness Harness(HostOperationScope operations) => new(new(() => [.. Sources.Select(source => new DiagnosticSourceRequirement(source.Game, source.Build))],
                [.. AllSources.Select(source => new DiagnosticSourceRequirement(source.Game, source.Build))]),
            new(this, this, null!, null!, null!, null!, this, _ => Task.CompletedTask, () => true), null!, this, this, this,
            new(this, this), operations, (reason, code) => { DisposedAtQuit = Disposed; Quits.Add((reason, code)); }, _ => { }, Errors.Add);
        internal List<string> Protocol { get; } = [];
        internal List<string> Visits { get; } = [];
        internal List<(int, string)> Bookmarks { get; } = [];
        internal List<string> Slots { get; } = [];
        internal List<string> Selections { get; } = [];
        internal List<string> Checks { get; } = [];
        internal List<string> Writes { get; } = [];
        internal CaptureDiagnosticContext Context(DiagnosticRunContext run) => new(run, this, this, _ => { });
        private void Record(string text) => Protocol.Add($"{Physics}/{Process} {text}");
        public void Key(CaptureKey key, bool pressed, CaptureKeyOrigin origin = CaptureKeyOrigin.Both, bool shift = false, char? unicode = null) => Record($"key {key} {pressed} {origin}");
        public void Pad(CaptureButton button, bool pressed)
        {
            Record($"pad {button} {pressed}");
            if (button == CaptureButton.Y) State = State with { QuickSelectOpen = pressed };
            if (button == CaptureButton.Start && pressed) State = State with { PauseOpen = true };
            if (button == CaptureButton.B && pressed) State = State with { Shots = State.Shots + 1 };
        }
        public void Axis(CaptureAxis axis, float value) => Record(FormattableString.Invariant($"axis {axis} {value}"));
        public void Mouse(bool pressed, Vector2 position) => throw new NotSupportedException();
        public ICaptureDiagnosticInput Acquire() => this;
        public void Dispose() { Disposed = true; Released.TrySetResult(); if (FailDispose) throw new IOException("fixture cleanup failure"); }
        public CaptureRuntimeDiagnostic Read() => State with { PhysicsTick = (long)Physics };
        public void ResizeWindow(int width, int height) => Record($"resize {width} {height}");
        public ICaptureBookmarkDiagnostic RequestBookmark(int visit, string destination)
        { Bookmarks.Add((visit, destination)); return new Bookmark(!MissingBookmark, destination); }
        private sealed record Bookmark(bool Exists, string Destination) : ICaptureBookmarkDiagnostic { public string ReadDestination() => Destination; }
        public void Save() => Saved = true;
        public DiagnosticFrameSnapshot ReadFrames() => new(Process, Physics);
        public Task NextAsync(DiagnosticFramePhase phase, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (phase == DiagnosticFramePhase.Physics) Physics++; else Process++;
            return HoldFrames ? NextFrame.Task.WaitAsync(cancellationToken) : Task.CompletedTask;
        }
        public ulong Milliseconds => Process + Physics;
        public Task DelayAsync(int milliseconds, CancellationToken token) => throw new NotSupportedException();
        public ImmutableArray<DiagnosticSourceSnapshot> ReadSources() => Sources;
        public Task<DiagnosticActionResult> NavigateAsync(string destinationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Visits.Add(destinationId);
            State = State with { Destination = destinationId, ProfileJson = MutateProfile ? "mutated" : State.ProfileJson };
            return Task.FromResult(new DiagnosticActionResult(true, null));
        }
        public bool AssignQuickSelect(int slot, string item) { Slots.Add($"{slot}:{item}"); return true; }
        public bool Select(string item) { Selections.Add(item); return true; }
        public void Assert(bool condition, string id, string detail)
        { if (!condition) throw new InvalidOperationException(id); Checks.Add(id); }
        public void Write(string phase) => Writes.Add(phase);
        public void Fail(string phase, Exception error, string reason) { Failures.Add((phase, error, reason)); Quits.Add((reason, 2)); }
    }
}
