using System.Collections.Immutable;
using System.Numerics;
using OBP.Game.Diagnostics;

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
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ReconciliationCaptureScenario(fixture.Context(run), fixture, fixture, fixture, fixture).RunAsync());
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
        internal bool Saved, MutateProfile, MissingBookmark, Disposed;
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
        public void Dispose() => Disposed = true;
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
            return Task.CompletedTask;
        }
        public ulong Milliseconds => Process + Physics;
        public Task DelayAsync(int milliseconds, CancellationToken token) => throw new NotSupportedException();
        public ImmutableArray<DiagnosticSourceSnapshot> ReadSources() => [];
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
        public void Fail(string phase, Exception error, string reason) => throw new NotSupportedException();
    }
}
