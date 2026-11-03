using System.Collections.Immutable;
using OBP.Core;
using OBP.Fusion;
using OBP.Game;
using OBP.Game.Diagnostics;
using OBP.RAC1;
using OBP.RAC1.Animation;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Audio;

namespace OBP.Tests;

public sealed class Rac1PlayerAudioPresenterTests
{
    [Fact]
    public void PlayerAndEquipmentBorrowSamplesAndShareTheOriginalPitchStream()
    {
        var playback = new Playback();
        using var audio = Create(playback);
        var sound = Sound(27) with { PitchMinimum = -800, PitchMaximum = 800, Gain = .37 };
        audio.Bind(Binding(), Assets(new() { [(0, 27)] = sound, [(121, 0)] = sound }), 121, true, true);
        audio.Play(new(0, 27));
        Assert.True(audio.PlayEquipmentSound(121, 0, new(2, 3, 4)));
        var expected = Rac1PickupRandom.FromSeed(121);
        foreach (var intent in playback.Started.Select(item => item.Intent).Concat(playback.Equipment))
        {
            Assert.Equal(sound.SelectPitch(expected).SampleRateHz, intent.Clip.SampleRateHz);
            Assert.Same(sound.Clip.InterleavedPcm16, intent.Clip.InterleavedPcm16);
            Assert.Equal(.37, intent.Gain);
        }
        Assert.Null(playback.Started[0].Intent.Position);
        Assert.Equal(new RuntimeAudioPosition(2, 3, 4), playback.Equipment[0].Position);
        Assert.Equal(new[] { 27, 0 }, audio.ReadSelected().Select(cue => cue.Sound));
    }

    [Fact]
    public void PauseRetainsLoopAndNativeCursorWithoutAnotherPitchDraw()
    {
        var playback = new Playback();
        using var audio = Create(playback);
        audio.Bind(Binding(), Assets(), 121, true, true);
        audio.Step(Glide, 0, false, true);
        Assert.Equal(2, audio.PackSound);
        var random = audio.Random;
        int stops = playback.Stopped.Count;
        audio.SetPaused(true);
        audio.Step(Glide with { NativeAction = 0, Grounded = true }, 0, false, true);
        audio.Play(new(0, 27));
        audio.StepReactions();
        audio.SetPaused(false);
        audio.Step(Glide, 0, false, true);
        Assert.Same(random, audio.Random);
        Assert.Equal(stops, playback.Stopped.Count);
        Assert.Single(playback.Started);
        Assert.Equal(new[] { true, false }, playback.Pauses);
        audio.Step(Glide with { NativeAction = 0, Grounded = true }, 0, false, true);
        Assert.Null(audio.PackSound);
        var stop = Assert.Single(audio.Events.ReadOutcomes().Where(cue => cue.Transition == DiagnosticAudioTransition.Stopped));
        Assert.Equal((ObpSourceGame.Rac1, Rac1Authority.Primary.BuildId, 0, 42L, 2),
            (stop.Source, stop.Build, stop.Level, stop.NativeTick, stop.Sound));
        Assert.Equal(("world", 1L), (stop.WorldInstanceId, stop.WorldGeneration));
        Assert.Single(audio.Events.Read().Where(cue => cue.Loop));
    }

    [Fact]
    public void WrenchFlightStartsOnceAndCatchStopsBeforeItsOneShot()
    {
        var playback = new Playback();
        using var audio = Create(playback);
        audio.Bind(Binding(), Assets(), 121, true, true);
        audio.StepWrench(0x15, 34, false, 10, false);
        audio.StepWrench(0x15, 40, false, 10, false);
        Assert.Equal(14, audio.ThrownWrenchSound);
        Assert.Single(audio.Events.Read().Where(cue => cue.Loop));
        audio.StepWrench(0x15, 50, false, 11, true);
        Assert.Null(audio.ThrownWrenchSound);
        audio.StepWrench(0x15, 84, false, 0, false);
        Assert.Equal(new[] { (14, DiagnosticAudioTransition.Started), (14, DiagnosticAudioTransition.Stopped), (5, DiagnosticAudioTransition.Started) },
            audio.Events.ReadOutcomes().Select(cue => (cue.Sound, cue.Transition)));
    }

    [Fact]
    public void DeathStopsMovementThenEquipmentAndLetsTheNativeReactionFinish()
    {
        var playback = new Playback();
        int equipmentStops = 0;
        using var audio = new Rac1PlayerAudioPresenter(playback, () => 42, () =>
        {
            Assert.Contains(playback.Stopped, item => item.Channel == Rac1PlayerAudioChannel.Pack);
            Assert.DoesNotContain(playback.Stopped, item => item.Channel == Rac1PlayerAudioChannel.Reaction);
            equipmentStops++;
        }, _ => { });
        audio.Bind(Binding(), Assets(), 121, true, true);
        audio.Step(Glide, 0, false, true);
        playback.Stopped.Clear();
        audio.NotifyDamage(4, 4);
        audio.NotifyDamage(1, 0);
        Assert.Equal(1, equipmentStops);
        Assert.Null(audio.PackSound);
        for (int i = 0; i < 150; i++) audio.StepReactions();
        Assert.Equal(new[] { 39, 31, 60, 35 }, audio.Events.Read().Where(cue => !cue.Loop).Select(cue => cue.Sound));
        Assert.All(playback.Started.Skip(1), item => Assert.Equal(Rac1PlayerAudioChannel.Reaction, item.Channel));
    }

    [Fact]
    public void RetirementAndSourceLossRejectFurtherPlaybackAndRebindStopsTheOldGeneration()
    {
        var playback = new Playback();
        using var audio = Create(playback);
        audio.Bind(Binding(), Assets(), 121, true, true);
        audio.Step(Glide, 0, false, true);
        audio.SetPaused(true);
        audio.RetireWorld();
        Assert.False(audio.Ready);
        Assert.Null(audio.Binding);
        audio.Play(new(0, 27)); audio.StepWrench(0x15, 34, false, 10, false);
        Assert.False(audio.PlayEquipmentSound(121, 0, new(0, 0, 0)));
        Assert.Single(playback.Started);
        var next = Binding() with { FileStamp = (1234, 5679), World = new("obp:fusion-lab", "second-world", 2, WorldEntryMode.OrdinaryFusion) };
        audio.Bind(next, Assets(), 121, true, true);
        audio.Step(Glide, 0, false, true);
        Assert.Equal(next, audio.Binding);
        Assert.Equal(2, playback.Started.Count);
        Assert.True(audio.PlayEquipmentSound(0, 27, new(0, 0, 0)));
        Assert.Single(playback.Equipment);
        audio.Bind(next with { World = next.World with { Generation = 3 } }, null, 121, true, true);
        Assert.False(audio.Ready);
        Assert.Null(audio.PackSound);
        audio.Play(new(0, 27));
        Assert.Equal(2, playback.Started.Count);
    }

    [Fact]
    public void DisposalImmediatelyReleasesEveryChannelAndCannotBeRebound()
    {
        var playback = new Playback();
        var audio = Create(playback);
        audio.Bind(Binding(), Assets(), 121, true, true);
        audio.Step(Glide, 0, false, true);
        playback.Stopped.Clear();
        audio.Dispose(); audio.Dispose();
        Assert.All(playback.Stopped, item => Assert.True(item.Immediately));
        Assert.Equal(5, playback.Stopped.Select(item => item.Channel).Distinct().Count());
        Assert.Throws<ObjectDisposedException>(() => audio.Bind(Binding(), Assets(), 121, true, true));
        audio.Play(new(0, 27)); audio.StepReactions();
        Assert.Single(playback.Started);
    }

    [Fact]
    public void SilentNativeProgramsAreEvidenceAndFailedPlaybackDoesNotClaimALoop()
    {
        var playback = new Playback { Accept = false };
        using var audio = Create(playback);
        audio.Bind(Binding(), Assets(new() { [(0, 2)] = Sound(2), [(0, 54)] = Sound(54) with { Clip = new(48000, 1, []) } }), 121, true, true);
        audio.Step(Glide, 0, false, true);
        Assert.Null(audio.PackSound);
        audio.Play(new(0, 54));
        Assert.Empty(audio.Events.Read());
        Assert.Equal(DiagnosticAudioPlayback.NativeSilence, Assert.Single(audio.Events.ReadOutcomes()).Playback);
        playback.Accept = true;
        audio.Step(Glide, 0, false, true);
        Assert.Equal(2, audio.PackSound);
        Assert.Equal(2, audio.Events.ReadOutcomes().Length);
    }

    [Fact]
    public void PresenterRetiresBothRootPartialsAndDiagnosticsCannotWalkPlayerAudioNodes()
    {
        foreach (string name in new[] { "Audio", "Rac1PlayerAudio" })
            Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "game", "scripts", $"OBPGame.{name}.cs")));
        string fixture = File.ReadAllText(Path.Combine(RepoPaths.Root, "game", "diagnostics", "GodotPlayerAudioFixture.cs"));
        Assert.DoesNotContain("GetChildren()", fixture);
        Assert.DoesNotContain("AudioStreamPlayer", fixture);
    }

    private static Rac1PlayerAudioPresenter Create(Playback playback) => new(playback, () => 42, () => { }, _ => { });
    private static Rac1PlayerAudioBinding Binding() => new(new(ObpSourceGame.Rac1, Rac1Authority.Primary.BuildId, "player-audio"),
        0, (1234, 5678), new("rac2:LEVEL0", "world", 1, WorldEntryMode.OrdinaryFusion));
    private static Rac1PlayerAudioSession.Facts Glide => new(new(19, 0, 1, 0, false), 1, 8, 0, 0, 2, false);
    private static Rac1CrateAudio.Sound Sound(int id) => new(id, new(48000, 1, [1, 2, 3]), 1, 60, 0, 0, 0, Loop: id is 2 or 14);
    private static Rac1PickupAssets.Catalogue Assets(Dictionary<(int, int), Rac1CrateAudio.Sound>? sounds = null)
    {
        sounds ??= new[] { 2, 5, 14, 27, 31, 35, 39, 60 }.ToDictionary(id => (0, id), Sound);
        var clips = new Dictionary<int, Rac1AnimationSoundClip>
        {
            [16] = Clip(16, (24, 33), (64, 39), (112, 31)) with { FrameDurations = Enumerable.Repeat(1d / 30d, 18).ToArray() },
            [69] = Clip(69, (8, 60), (232, 35)) with { FrameDurations = Enumerable.Repeat(1d / 15d, 25).ToArray() },
        };
        return new(new Dictionary<int, Rac1PickupAssets.Model>(), [], sounds, new(clips, new Dictionary<int, Rac1CrateAudio.Sound>()));
    }
    private static Rac1AnimationSoundClip Clip(int sequence, params (ushort Position, ushort Sound)[] cues) => new(
        sequence, null, cues.Select(cue => new Rac1MobyAnimation.TimedSoundCue((uint)(cue.Position << 16) | cue.Sound, cue.Position, cue.Sound)).ToArray(),
        Enumerable.Range(0, 64).Select(frame => frame * 3d).ToArray());

    private sealed class Playback : IRac1PlayerAudioPlayback
    {
        internal bool Accept = true;
        internal readonly List<(Rac1PlayerAudioChannel Channel, RuntimeAudioPlaybackIntent Intent)> Started = [];
        internal readonly List<RuntimeAudioPlaybackIntent> Equipment = [];
        internal readonly List<(Rac1PlayerAudioChannel Channel, bool Immediately)> Stopped = [];
        internal readonly List<bool> Pauses = [];
        public bool Play(Rac1PlayerAudioChannel channel, RuntimeAudioPlaybackIntent intent)
        { if (!Accept) return false; Started.Add((channel, intent)); return true; }
        public bool PlayEquipment(RuntimeAudioPlaybackIntent intent) { Equipment.Add(intent); return true; }
        public void Stop(Rac1PlayerAudioChannel channel, bool immediately) => Stopped.Add((channel, immediately));
        public void SetPaused(bool paused) => Pauses.Add(paused);
        public ImmutableArray<DiagnosticAudioVoice> ReadVoices() => [];
    }
}
