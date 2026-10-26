using System.Collections.Immutable;
using System.Text.Json;
using OBP.Core;
using OBP.Game;
using OBP.Game.Diagnostics;
using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class PlayerDiagnosticTests
{
    private static DiagnosticScenarioDefinition Definition => new("synthetic-player-trial/v1", DiagnosticSourceAccess.AssetFree,
        DiagnosticSetup.AssetFreeFixture, [], TimeSpan.FromSeconds(10), 1000);
    private sealed class Sources : IDiagnosticSources { public ImmutableArray<DiagnosticSourceSnapshot> ReadSources() => []; }
    private sealed class Clock : IDiagnosticFrameClock
    {
        public DiagnosticFrameSnapshot ReadFrames() => new(0, 0);
        public Task NextAsync(DiagnosticFramePhase phase, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class TickFixture : IDiagnosticCapabilitySamples, IDiagnosticPlayerInputFixture, IDiagnosticPlayerInputLease
    {
        private Action? _sample;
        private Func<int, DiagnosticPlayerInputFrame> _filter = _ => new();
        private int _ordinal;
        public int Tick { get; private set; }
        public int Subscribers { get; private set; }
        public bool WrongClock { get; set; }
        public bool Disposed { get; private set; }
        public List<DiagnosticPlayerInputFrame> Inputs { get; } = [];
        public DiagnosticCapabilityFrame Read() => new(Tick + (WrongClock ? 1 : 0), 1000 + Tick, Tick / 60d,
            Tick < 80 ? "idle" : Tick < 160 ? "run" : "jump", Tick / 6, [Tick, 0], "attachment", .25,
            new(Tick, 2, 3), new(4, 5, 6), 0, 1);
        public IDiagnosticPlayerInputLease Acquire() => this;
        public void SetFilter(Func<int, DiagnosticPlayerInputFrame> input) { _filter = input; _ordinal = 0; }
        public bool Neutral => _filter(0) == new DiagnosticPlayerInputFrame();
        public void Pulse() { Inputs.Add(_filter(_ordinal++)); Tick++; _sample?.Invoke(); }
        public IDisposable SubscribeAfterPhysics(Action sample)
        {
            _sample += sample; Subscribers++;
            return new Cleanup(() => { _sample -= sample; Subscribers--; });
        }
        public void Dispose() { Disposed = true; }
    }
    private sealed class Cleanup(Action close) : IDisposable { public void Dispose() => close(); }

    [Fact]
    public async Task CapabilityTrialRetainsExactly240NativeTicksAndFortyOrderedSamples()
    {
        var fixture = new TickFixture();
        using var run = new DiagnosticRunContext(new Clock(), CancellationToken.None, 1000);
        var trial = PlayerCapabilityTrial.CaptureAsync(run, fixture, fixture);
        for (int tick = 0; tick < 239; tick++) fixture.Pulse();
        Assert.False(trial.IsCompleted);
        fixture.Pulse();
        var result = await trial;
        Assert.Equal(40, result.Length);
        Assert.Equal(Enumerable.Range(1, 40).Select(index => index * 6), result.Select(sample => sample.Tick));
        Assert.Equal(4, result[^1].Clock);
        Assert.Equal(3, result.Select(sample => sample.Sequence).Distinct().Count());
        Assert.Equal(1, fixture.Inputs.Count(frame => frame.ActionJustPressed));
        Assert.True(fixture.Inputs[210].ActionJustPressed);
        Assert.True(fixture.Neutral); Assert.Equal(0, fixture.Subscribers);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result[0]));
        Assert.Equal(new[] { "Tick", "Sequence", "Frame", "Pose", "Attachment", "Facing", "Position", "Camera", "Clock" },
            json.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task CanceledNativeTrialUnsubscribesAndDisposesItsInputLease()
    {
        var fixture = new TickFixture(); using var lifetime = new CancellationTokenSource();
        var trial = new DiagnosticScenarioRunner(new Sources(), new Clock()).RunAsync(
            new StandalonePlayerCapabilityTrial(Definition, fixture, fixture), true, lifetime.Token);
        for (int tick = 0; tick < 12; tick++) fixture.Pulse();
        Assert.Equal(1, fixture.Subscribers); lifetime.Cancel();
        var result = await trial;
        Assert.Equal(DiagnosticRunStatus.Canceled, result.Status);
        Assert.True(fixture.Disposed); Assert.True(fixture.Neutral); Assert.Equal(0, fixture.Subscribers);
    }

    [Fact]
    public async Task AClockMismatchFailsAndStillReleasesTheSampler()
    {
        var fixture = new TickFixture { WrongClock = true };
        var trial = new DiagnosticScenarioRunner(new Sources(), new Clock()).RunAsync(
            new StandalonePlayerCapabilityTrial(Definition, fixture, fixture), true, CancellationToken.None);
        fixture.Pulse(); var result = await trial;
        Assert.Equal(DiagnosticRunStatus.Failed, result.Status);
        Assert.Contains("duplicate/missing", result.Detail, StringComparison.Ordinal);
        Assert.True(fixture.Disposed); Assert.True(fixture.Neutral); Assert.Equal(0, fixture.Subscribers);
    }

    [Fact]
    public void AudioEvidenceKeepsSourceBuildNativeClockAndImmutableSnapshots()
    {
        var collector = new DiagnosticPlayerAudioCollector(2);
        var cue = new DiagnosticPlayerAudioEvent(ObpSourceGame.Rac1, "retail-authority", 0, 142, 71, 2, false, false);
        collector.Record(cue); var before = collector.Read();
        collector.Record(cue with { NativeTick = 143, Loop = true });
        Assert.Equal(cue, Assert.Single(before));
        Assert.Equal(new long[] { 142, 143 }, collector.Read().Select(item => item.NativeTick));
        collector.Clear(); Assert.Empty(collector.Read()); Assert.Single(before);
    }

    [Fact]
    public void AudioOverflowInvalidatesEvidenceUntilTheNextExplicitFixtureReset()
    {
        var collector = new DiagnosticPlayerAudioCollector(1);
        var cue = new DiagnosticPlayerAudioEvent(ObpSourceGame.Rac1, "retail-authority", 0, 1, 0, 26, false, false);
        collector.Record(cue); collector.Record(cue);
        Assert.Throws<DiagnosticUnavailableException>(() => collector.Read());
        collector.Clear(); collector.Record(cue);
        Assert.Equal(cue, Assert.Single(collector.Read()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticPlayerAudioCollector(0));
    }

    [Fact]
    public void ASourceSelectedSilentAlternativeIsAnOutcomeAndNeverPretendsToBePlayback()
    {
        var collector = new DiagnosticPlayerAudioCollector();
        var voice = new DiagnosticPlayerAudioEvent(ObpSourceGame.Rac1, "retail-authority", 0, 20, 0, 54, false, false)
        { Playback = DiagnosticAudioPlayback.NativeSilence };
        collector.Record(voice);
        Assert.Empty(collector.Read()); Assert.Equal(voice, Assert.Single(collector.ReadOutcomes()));
        collector.Record(voice with { Playback = DiagnosticAudioPlayback.Played, NativeTick = 40 });
        Assert.Equal(40, Assert.Single(collector.Read()).NativeTick); Assert.Equal(2, collector.ReadOutcomes().Length);
    }

    [Fact]
    public void SyntheticRac1AudioSurfacesUseOnlyTheirExplicitMaterialAuthority()
    {
        var floor = Rac1DiagnosticCollision.Create([0, 0, 0, 1, 0, 0, 0, 0, 1], [0, 1, 2], [43]);
        Assert.Equal(OBP.RAC1.Rac1Authority.Primary, floor.MaterialSource);
        Assert.Equal(43, OBP.Godot.RuntimeWorldScene.MaterialIdForFace(floor, [0], 0, OBP.RAC1.Rac1Authority.Primary));
        Assert.Null(OBP.Godot.RuntimeWorldScene.MaterialIdForFace(floor, [0], 0, OBP.RAC2.Rac2Authority.Primary));
    }

    [Fact]
    public void ProfileFixtureStagesTheOriginalSourceAmmoHealthAndWalletWitness()
    {
        var native = new PlayerSessionNativeState(Rac1WeaponInventory.CreateOpeningLoadout(), 0, 1440);
        var fixture = new DiagnosticSharedProfileFixture(() => new(true, true, false, false, "rac1:LEVEL0", "rac1:LEVEL0", 3), () => native);
        fixture.StageSavedPlayer(); var state = fixture.Read();
        Assert.Equal(17, state.LancerAmmo); Assert.Equal(9, state.CannonAmmo); Assert.Equal(3, state.NativeNanotech);
        Assert.Equal(42, native.Wallet.Balance);
        Assert.Equal(3, state.SavedSources); Assert.False(state.WriteBlocked);
    }

    [Fact]
    public void AllPlayerAliasesRemainExplicitAndAllFourOldPartialsAreRetired()
    {
        foreach (var (flag, tokens) in new[] { ("player-session-smoke", new[] { "--player-session-smoke" }),
            ("player-capabilities-smoke", new[] { "--player-capabilities-smoke" }), ("shared-profile-smoke", new[] { "--profile-smoke", "read" }) })
        {
            var parsed = CommandLineArgs.Parse(tokens);
            Assert.True(PlayerSessionDiagnosticHarness.Registry.Find(flag).Requested(parsed));
            Assert.True(PlayerSessionDiagnosticHarness.IsRequested(parsed));
        }
        Assert.False(PlayerSessionDiagnosticHarness.IsRequested(new()));
        Assert.Equal(4, PlayerSessionDiagnosticHarness.Registry.Registrations.Length);
        Assert.Equal("rac1-player-audio-smoke", WorldSmokeHarness.Registry.Find("--rac1-player-audio-smoke").Id);
        foreach (string name in new[] { "PlayerSessionSmoke", "PlayerCapabilitiesSmoke", "SharedProfileSmoke", "Rac1PlayerAudioSmoke" })
            Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "game", "diagnostics", "OBPGame." + name + ".cs")));
    }
}
