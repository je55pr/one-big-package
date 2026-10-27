using System.Collections.Immutable;
using OBP.Core;
using OBP.Game.Diagnostics;
using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1WorldRouteDiagnosticTests
{
    [Fact]
    public async Task RepeatedVisitsDrainOldResourcesAndRestoreTheTemporarilyDetachedSource()
    {
        var fixture = new Fixture(); var result = await fixture.RunAsync(music: false);
        Assert.Equal(DiagnosticRunStatus.Passed, result.Status);
        Assert.Equal(2, fixture.Exits); Assert.Equal(2, fixture.Entries); Assert.Equal(1, fixture.Detaches); Assert.Equal(1, fixture.Restores);
        Assert.False(fixture.Detached); Assert.Equal(3, fixture.Generation);
        Assert.Contains(fixture.Logs, line => line.Contains("retirement PASS", StringComparison.Ordinal));
    }
    [Theory]
    [InlineData(true, 0, 0, false)]
    [InlineData(false, 1, 0, false)]
    [InlineData(false, 0, 1, false)]
    [InlineData(false, 0, 0, true)]
    public async Task LiveOldSceneHullsAudioOrCameraCannotPassAWorldRetirement(bool scene, int hulls, int audio, bool camera)
    {
        var fixture = new Fixture { OldScene = scene, OldHulls = hulls, OldAudio = audio, OldCamera = camera };
        var result = await fixture.RunAsync(music: false);
        Assert.Equal(DiagnosticRunStatus.Failed, result.Status); Assert.Contains("old route lease", result.Detail, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Entries); Assert.False(fixture.Detached); Assert.Equal(1, fixture.Restores);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AReusedIdentityOrDuplicatedPopulationCannotPassReentry(bool identity)
    {
        var fixture = new Fixture { ReuseIdentity = identity, DuplicatePopulation = !identity };
        var result = await fixture.RunAsync(music: false);
        Assert.Equal(DiagnosticRunStatus.Failed, result.Status);
        Assert.Contains(identity ? "world identity" : "duplicated or lost", result.Detail, StringComparison.Ordinal);
        Assert.False(fixture.Detached);
    }
    [Fact]
    public async Task AnotherDestinationFromTheSameSourceCannotImpersonateReentry()
    {
        var fixture = new Fixture { WrongDestination = true }; var result = await fixture.RunAsync(music: false);
        Assert.Equal(DiagnosticRunStatus.Failed, result.Status); Assert.Contains("world identity", result.Detail, StringComparison.Ordinal);
        Assert.False(fixture.Detached);
    }
    [Fact]
    public async Task AReplacedStableWorldIdCannotPassWithOnlyAFreshGeneration()
    {
        var fixture = new Fixture { ReplaceWorldId = true }; var result = await fixture.RunAsync(music: false);
        Assert.Equal(DiagnosticRunStatus.Failed, result.Status); Assert.Contains("world identity", result.Detail, StringComparison.Ordinal);
    }
    [Fact]
    public async Task MissingDecodedOpeningMusicCannotPassTheFreshResourceCensus()
    {
        var fixture = new Fixture { MissingDecodedMusic = true }; var result = await fixture.RunAsync(music: false);
        Assert.Equal(DiagnosticRunStatus.Failed, result.Status); Assert.Contains("duplicated or lost", result.Detail, StringComparison.Ordinal);
    }
    [Fact]
    public async Task ReentryUsesTheFreshEntryBaselineRatherThanTheWarmedScenarioCache()
    {
        var fixture = new Fixture { WarmPrimaryCache = true }; var result = await fixture.RunAsync(music: false);
        Assert.Equal(DiagnosticRunStatus.Passed, result.Status); Assert.Equal(2, fixture.Entries);
    }
    [Fact]
    public async Task ADetachedButUnfreedNodeCannotPassTheRetirementProbe()
    {
        var fixture = new Fixture { InvalidDetachedProbe = true }; var result = await fixture.RunAsync(music: false);
        Assert.Equal(DiagnosticRunStatus.Failed, result.Status); Assert.Contains("Detached but unfreed", result.Detail, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Exits); Assert.Equal(0, fixture.Detaches);
    }
    [Fact]
    public async Task CancellationDuringQueuedCleanupRestoresTheSourceWithoutReportingPass()
    {
        using var stop = new CancellationTokenSource(); var fixture = new Fixture { StopDuringCleanup = stop };
        var result = await fixture.RunAsync(music: false, stop.Token);
        Assert.Equal(DiagnosticRunStatus.Canceled, result.Status); Assert.False(fixture.Detached); Assert.Equal(1, fixture.Restores);
        Assert.Equal(0, fixture.Entries); Assert.DoesNotContain(fixture.Logs, line => line.Contains("retirement PASS", StringComparison.Ordinal));
    }
    [Fact]
    public async Task AnOrdinarySharedSaveIsRejectedBeforeAnyWorldOrSourceAction()
    {
        var fixture = new Fixture { SharedProfile = true }; var result = await fixture.RunAsync(music: false);
        Assert.Equal(DiagnosticRunStatus.Unavailable, result.Status); Assert.Contains("--no-profile", result.Detail, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Exits); Assert.Equal(0, fixture.Detaches); Assert.Equal(0, fixture.Entries);
    }
    [Fact]
    public async Task MusicUsesTheOriginalIntroDurationAndCrankOrderWithTheExistingFramePhases()
    {
        var fixture = new Fixture(); var result = await fixture.RunAsync(music: true);
        Assert.Equal(DiagnosticRunStatus.Passed, result.Status); Assert.Equal(new[] { 296, 297 }, fixture.Cranks);
        Assert.Equal(2.6, fixture.MusicDelta, 10); Assert.Equal(9, fixture.PhysicsFrames);
        Assert.Equal(Rac1NovalisMusic.Area.Enemy, fixture.Area); Assert.Equal(0, fixture.Slot);
    }
    [Fact]
    public async Task MusicCannotPassWhenTheObservedLoopWasNotSelected()
    {
        var fixture = new Fixture { RefuseLoop = true }; var result = await fixture.RunAsync(music: true);
        Assert.Equal(DiagnosticRunStatus.Failed, result.Status); Assert.Contains("Cave intro never switched", result.Detail, StringComparison.Ordinal);
        Assert.Equal(new[] { 296 }, fixture.Cranks);
    }
    private sealed class Release(Action release) : IDisposable { public void Dispose() => release(); }
    private sealed class Scenario(Func<DiagnosticRunContext, Task> execute) : IDiagnosticScenario
    {
        public DiagnosticScenarioDefinition Definition { get; } = new("world-route-fixture/v1", DiagnosticSourceAccess.AssetFree, DiagnosticSetup.AssetFreeFixture, [], TimeSpan.FromSeconds(30), 100);
        public Task RunAsync(DiagnosticRunContext run) => execute(run);
    }
    private sealed class Fixture : IRac1WorldRouteLifetimeFixture, IDiagnosticFrameClock, IDiagnosticSources, IDiagnosticMenuFixture,
        IDiagnosticNavigationActions, IDiagnosticSourceAttachmentFixture, IDiagnosticMovementWorld, IRac1NovalisMusicFixture
    {
        public int Exits, Entries, Detaches, Restores, Generation = 1, PhysicsFrames;
        public int OldHulls, OldAudio; public bool OldScene, OldCamera, ReuseIdentity, DuplicatePopulation, WrongDestination, SharedProfile, RefuseLoop;
        public bool ReplaceWorldId, MissingDecodedMusic, WarmPrimaryCache, InvalidDetachedProbe;
        private int _decoded = 1;
        public bool Detached; private bool _title;
        public CancellationTokenSource? StopDuringCleanup;
        public List<string> Logs { get; } = []; public List<int> Cranks { get; } = [];
        public double MusicDelta; public int Slot; public Rac1NovalisMusic.Area Area = Rac1NovalisMusic.Area.Enemy;
        private DiagnosticWorldSnapshot World => new(_title ? null : WrongDestination && Entries > 0 ? "rac1:LEVEL3" : "rac1:LEVEL1", _title ? null : ObpSourceGame.Rac1,
            _title ? null : ReplaceWorldId && Entries > 0 ? "replacement" : "obp-world", Generation);
        public Rac1WorldRouteResources Read() => new(World, _title ? 0 : DuplicatePopulation && Entries > 0 ? 36 : 18,
            0, 0, _title ? 0 : 9, _title ? 0 : _decoded, !_title, false, false, SharedProfile);
        public IRac1WorldRouteRetirementWitness Capture() => new Witness(this);
        public IRac1WorldRouteDetachedProbe CreateDetachedProbe() => new Probe(InvalidDetachedProbe);
        private sealed class Probe(bool invalid) : IRac1WorldRouteDetachedProbe, IRac1WorldRouteRetirementWitness
        {
            private bool _freed;
            public IRac1WorldRouteRetirementWitness Witness => this;
            public Rac1WorldRouteRetirement Read() => _freed || invalid ? new(true, 0, 0, true) : new(false, 1, 1, false);
            public void Dispose() => _freed = true;
        }
        private sealed class Witness(Fixture fixture) : IRac1WorldRouteRetirementWitness
        { public Rac1WorldRouteRetirement Read() => new(!fixture.OldScene, fixture.OldHulls, fixture.OldAudio, !fixture.OldCamera); }
        public void ShowTitle() { _title = true; Exits++; }
        public Task<DiagnosticActionResult> NavigateAsync(string destinationId, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); if (Detached) throw new InvalidOperationException("A detached source was used for entry.");
            Entries++; _title = false; if (!ReuseIdentity) Generation++;
            _decoded = MissingDecodedMusic ? 0 : 1;
            return Task.FromResult(new DiagnosticActionResult(true, null));
        }
        public IDisposable Detach(ObpSourceGame game, bool refreshOnDetach)
        {
            Assert.Equal(ObpSourceGame.Rac1, game); Assert.False(refreshOnDetach); Assert.True(_title);
            Detached = true; Detaches++; return new Release(() => { Detached = false; Restores++; });
        }
        public DiagnosticFrameSnapshot ReadFrames() => new(0, (ulong)PhysicsFrames);
        public Task NextAsync(DiagnosticFramePhase phase, CancellationToken token)
        {
            if (Detached) StopDuringCleanup?.Cancel(); token.ThrowIfCancellationRequested();
            if (phase == DiagnosticFramePhase.Physics) PhysicsFrames++; return Task.CompletedTask;
        }
        public ImmutableArray<DiagnosticSourceSnapshot> ReadSources() => [];
        DiagnosticMovementWorld IDiagnosticMovementWorld.Read() => new(ObpSourceGame.Rac1, 1, null, 0, false);
        public IDisposable LimitFrameRate(int rate) => new Release(() => { });
        Rac1NovalisMusicDiagnostic IRac1NovalisMusicFixture.Read() => new(World,
            [.. Enumerable.Range(0, 9).Select(index => new Rac1NovalisMusicTrackDiagnostic(index, (uint)index, index + .5))],
            Area, Slot, Slot == 3 ? 0 : 1, 3, ["level Music started (2D)"], "fixture");
        public void SelectAuthoredCrank(int index) { Cranks.Add(index); Area = index == 296 ? Rac1NovalisMusic.Area.Cave : Rac1NovalisMusic.Area.Enemy; Slot = index == 296 ? 2 : 0; }
        public void Tick(double delta) { MusicDelta = delta; if (!RefuseLoop) Slot = 3; }
        public async Task<DiagnosticRunResult> RunAsync(bool music, CancellationToken token = default)
        {
            var actions = new DiagnosticInputActions(_ => true, (_, _) => { }); var physical = new DiagnosticPhysicalInput((_, _) => { }, (_, _) => { });
            var ports = new MovementScenarioPorts(null!, null!, null!, this, actions, physical, this, Logs.Add, Logs.Add);
            var scenario = new Scenario(async run =>
            {
                using var driver = new MovementDiagnosticDriver(run, actions, physical); var context = new MovementDiagnosticContext(ports, driver);
                var entry = Read(); if (WarmPrimaryCache) _decoded = 3;
                if (music) await new Rac1NovalisMusicScenario(context, this).RunAsync();
                else await new Rac1WorldRouteLifetimeScenario(this, this, this, this, Logs.Add).RunAsync(context, entry);
            });
            return await new DiagnosticScenarioRunner(this, this).RunAsync(scenario, true, token);
        }
        public DiagnosticNavigationSnapshot ReadNavigation() => throw new NotSupportedException();
        public DiagnosticStartupPresentation ReadStartupPresentation() => throw new NotSupportedException();
        public ImmutableArray<string> ReadVisibleText(DiagnosticMenuSurface surface) => throw new NotSupportedException();
        public void ShowWorldSelect() => throw new NotSupportedException();
        public bool Back() => throw new NotSupportedException();
        public void PressTitleStart() => throw new NotSupportedException();
        public void PressLab() => throw new NotSupportedException();
        public bool OpenPause() => throw new NotSupportedException();
        public bool ClosePause() => throw new NotSupportedException();
        public void ShowPauseSection(string section) => throw new NotSupportedException();
    }
}
