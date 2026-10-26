using System.Buffers.Binary;
using System.Security.Cryptography;
using OBP.IO;
using OBP.RAC1.Animation;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1PlayerAudioTests
{
    [Fact]
    public void LevelSoundDefinitionsResolveTheirOwnRemapAndRejectUnboundedSlots()
    {
        byte[] index = new byte[0x200];
        void I(int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(at), value);
        void H(int at, short value) => BinaryPrimitives.WriteInt16LittleEndian(index.AsSpan(at), value);
        I(0x70, 0xc0); H(0xc0, 0x20); H(0xc2, 2); H(0xc4, 0x80); H(0xc6, 2);
        H(0xfa, 1); H(0x11a, -1); I(0x140, 17); I(0x144, 42);
        var result = Rac1LevelSoundRemap.Read(index);
        Assert.Equal([42, -1], result.Select(entry => entry.BankSoundId));
        Assert.Equal([0xe0, 0x100], result.Select(entry => entry.DefinitionOffset));
        H(0xfa, 2);
        Assert.Throws<InvalidDataException>(() => Rac1LevelSoundRemap.Read(index));
        H(0xfa, 1); I(0x144, -2);
        Assert.Throws<InvalidDataException>(() => Rac1LevelSoundRemap.Read(index));
        I(0x144, 42); H(0xc2, 100);
        Assert.Throws<InvalidDataException>(() => Rac1LevelSoundRemap.Read(index));
        Assert.Throws<InvalidDataException>(() => Rac1LevelSoundRemap.Read(new byte[0x70]));
    }

    [Fact]
    public void AnimationSoundsUseQuantizedKeyframesAndOnlyTheFirstCrossedEvent()
    {
        var clip = Clip(7, (8, 26), (8, 62), (16, 30));
        var cursor = new Rac1AnimationSoundCursor(new Dictionary<int, Rac1AnimationSoundClip> { [7] = clip });
        Assert.Null(cursor.Step(Sample(7, 0, .4f), 1));
        Assert.Null(cursor.Step(Sample(7, 0, .499f), 1));
        Assert.Equal(26, cursor.Step(Sample(7, 0, .5f), 1));
        Assert.Null(cursor.Step(Sample(7, 0, .9f), 1));
        Assert.Equal(30, cursor.Step(Sample(7, 1, 0), 1));
        Assert.Null(cursor.Step(Sample(7, 1, .2f), 1));
        // Timeline coordinates are deliberately 3x the keyframe indices.
        Assert.Equal(3, clip.Timeline[1]);
    }

    [Fact]
    public void BlendsWrapsSelectionsAndResetsDoNotReplayOrCatchUpOldSounds()
    {
        var clips = new Dictionary<int, Rac1AnimationSoundClip> { [23] = Clip(23, (8, 27)) };
        var cursor = new Rac1AnimationSoundCursor(clips);
        Assert.Null(cursor.Step(Sample(23, 0, .4f, true), 1));
        Assert.Null(cursor.Step(Sample(23, 0, .8f, true), 1));
        Assert.Null(cursor.Step(Sample(23, 0, .1f), 1));
        Assert.Equal(27, cursor.Step(Sample(23, 0, .6f), 1));
        Assert.Null(cursor.Step(Sample(23, 19, .9f), 1));
        Assert.Null(cursor.Step(Sample(23, 0, .2f), 1));
        Assert.Equal(27, cursor.Step(Sample(23, 0, .6f), 1));
        Assert.Null(cursor.Step(Sample(23, 0, .9f), 2));
        cursor.Reset();
        Assert.Null(cursor.Step(Sample(23, 0, .9f), 2));
    }

    [Fact]
    public void FootstepsUseAuthoredTimeStrictCrossingsAndTheNativeStartupDelay()
    {
        var clip = Clip(3);
        var session = new Rac1PlayerAudioSession(new Dictionary<int, Rac1AnimationSoundClip> { [3] = clip });
        Rac1PlayerAudioSession.Frame Step(int age, int frame, float fraction) => session.Step(0,
            new(Sample(3, frame, fraction), 1, 2, age, 1, 0, true), 0);
        Assert.Empty(Step(14, 5, .5f).OneShots);
        var right = Assert.Single(Step(15, 5, .8f).OneShots);
        Assert.Equal(new(0, 8, true), right); // surface1, right foot: 2+4+2.
        Assert.Empty(Step(21, 5, .9f).OneShots);
        Assert.Equal(new(0, 6, true), Assert.Single(Step(22, 5, .9f).OneShots));
        Assert.Empty(Step(23, 5, .9f).OneShots);
        Assert.Empty(Step(40, 16, .5f).OneShots); // exactly49.5 is not crossed yet.
        Assert.Equal(new(0, 6, true), Assert.Single(Step(41, 16, .6f).OneShots));
        session.Reset();
        Assert.Empty(Step(40, 16, .6f).OneShots); // Loading a state does not emit historical steps.
    }

    [Theory]
    [InlineData(0, 0, 0, false, 2)]
    [InlineData(0, 2, 1, false, 12)]
    [InlineData(1, 3, 1, false, 18)]
    [InlineData(2, 0, 0, true, 9)]
    [InlineData(12, 0, 1, false, 4)]
    public void FootstepAddressIncludesLevelSurfaceFootAndBoots(int level, int surface, int foot, bool boots, int expected) =>
        Assert.Equal(expected, Rac1PlayerAudioSession.FootstepIndex(level, surface, foot, boots));

    [Fact]
    public void FastLoopWrapUsesNativeFractionalWindowWithoutDroppingTheRightStep()
    {
        double[] durations = Enumerable.Repeat(1d / 15d, 14).ToArray();
        var clock = new Rac1AnimationFrameClock(new Dictionary<int, IReadOnlyList<double>> { [4] = durations }, 4, 0, 1);
        clock.ResumePlayback(4, 13, .9f, 2.5f);
        var clip = Clip(4) with { Timeline = Enumerable.Range(0, 14).Select(frame => frame * 2d).ToArray() };
        var session = new Rac1PlayerAudioSession(new Dictionary<int, Rac1AnimationSoundClip> { [4] = clip });
        session.Step(0, new(clock.Current, 1, 2, 100, 0, 0, true), 0);
        var next = clock.Advance();
        Assert.InRange(clock.FractionAdvanceThisUpdate, .62499f, .62501f);
        Assert.Equal(0, next.CurrentFrame);
        var result = session.Step(0, new(next, 1, 2, 101, 0, 0, true,
            FractionAdvance: clock.FractionAdvanceThisUpdate), 0);
        Assert.Equal(new(0, 4, true), Assert.Single(result.OneShots));
    }

    [Fact]
    public void GlideLoopsChangeWithPackAndStopWhenLeavingTheAction()
    {
        var session = new Rac1PlayerAudioSession(new Dictionary<int, Rac1AnimationSoundClip>());
        var facts = new Rac1PlayerAudioSession.Facts(Sample(19, 0, 0), 1, 8, 0, 0, 2, false);
        Assert.Equal(2, session.Step(0, facts, 0).PackLoop);
        Assert.Equal(18, session.Step(0, facts with { PackId = 3 }, 0).PackLoop);
        Assert.Null(session.Step(0, facts with { NativeAction = 7 }, 0).PackLoop);
        Assert.Null(session.Step(0, facts with { MovementAction = 7 }, 0).PackLoop); // The fading pose may still name glide.
        Assert.Equal(6, session.Step(0, facts with { NativeAction = 0x31 }, 0).PackLoop);
        Assert.Empty(session.Step(0, facts with { NativeAction = 6 }, 0).OneShots);
        Assert.Equal(2, session.Step(0, facts with { NativeAction = 0, Grounded = true }, 0).OneShots.Count);
    }

    [Fact]
    public void WrenchImpactGateFlightLoopCatchAndHyperStrikeAreSeparateEvents()
    {
        Assert.True(Rac1PlayerAudioSession.WrenchVictimSoftness(749));
        Assert.False(Rac1PlayerAudioSession.WrenchVictimSoftness(1440));
        Assert.Null(Rac1PlayerAudioSession.WrenchVictimSoftness(500));
        var session = new Rac1PlayerAudioSession(new Dictionary<int, Rac1AnimationSoundClip>());
        var facts = new Rac1PlayerAudioSession.Facts(Sample(26, 0, 0), 1, 0x15, 0, 0, 0, true);
        session.Step(0, facts, 1);
        Assert.Equal(new(71, 1), session.WrenchContact(soft: true));
        Assert.Null(session.WrenchContact(soft: false));
        session.Step(0, facts, 2);
        Assert.Equal(new(71, 2), session.WrenchContact(soft: false, world: true));
        Assert.Equal(14, session.StepWrench(0x15, 34, false, 10, false).Loop);
        Assert.Null(session.WrenchContact(false));
        for (int i = 0; i < 30; i++) session.Step(0, facts, 2);
        Assert.Equal(new(71, 0), session.WrenchContact(false));
        Assert.Null(session.StepWrench(0x15, 50, false, 11, true).Loop);
        Assert.Equal(new(0, 5), Assert.Single(session.StepWrench(0x15, 84, false, 0, false).OneShots));
        Assert.Empty(session.StepWrench(0x15, 85, false, 0, false).OneShots);
        session.Step(0, facts, 3);
        Assert.Empty(session.StepWrench(0x14, 26, false, 0, false).OneShots);
        Assert.Equal(new(71, 2), Assert.Single(session.StepWrench(0x14, 28, false, 0, false).OneShots));
        Assert.Empty(session.StepWrench(0x14, 29, false, 0, false).OneShots);
        session.Reset();
        Assert.Empty(session.StepWrench(0, 0, false, 0, false).OneShots);
    }

    [SkippableFact]
    public void CanonicalExecutablePinsSoundDispatchFootstepsSurfacesAndLevelBases()
    {
        var read = Rac1AirborneNativeSourceTests.ReadOverlay();
        foreach (var (start, end, expected) in new[]
        {
            (0x1f3608, 0x1f3638, "270aa0c2e81971cf9a2146f480af944e130686e436371a77d015c409d606db6f"),
            (0x205f60, 0x206080, "d065eb5b85d3f09e564c9d3beee8bfd433300cea95763e9f4269799cf67340f8"),
            (0x206080, 0x2061e0, "038911ecd3a179f3bf4e9de293a10165b1dd1d42544d32e260e36dca13804510"),
            (0x211870, 0x2118a8, "b09d2dd5acc0de7a1a2e2cff31bf341278774010583a92bfcc28e33de3ad99cb"),
            (0x232670, 0x232ab0, "fd390c4fde8c724da453107bc5c33a2d5a65335ae377a5c54eca21a64ce3bd95"),
            (0x28de68, 0x28df20, "8047a1d870ff5db0be379a14ffd7b9db3e8b0859f46af382609eb236df8fdcd0"),
            (0x2a7d90, 0x2a7de0, "e867a7c5d28d873ca6aa0d31793a98851dc5306f174d4be8bda9a717c524bb49"),
            (0x2a84c8, 0x2a98b8, "8a3881ff57d07b98ef6f753660d3279f6d6786db12ca5e6dbb712d94e9854d69"),
            (0x22667c, 0x22668c, "948cb2bb66b8f512d4fcec8d46acf6ed8f8542bf38d260bfc0d60b368438c0d1"),
            (0x227188, 0x2271d8, "d72c31400e6cbe46fdda2682e56571434eb8a803d2fd00f2b64c1364d7e3ef09"),
            (0x17beb8, 0x17bec4, "8fa5ad727ed4cd9444c4660ca8ff915c7519732bc0f03e9caf91a0383f80e702"),
            (0x22f858, 0x22f940, "dd62a785d2fd9fb8c267d02958446975ae3f8cb89d378aabef5e2330a45008c1"),
            (0x2227f0, 0x222834, "2075acc7e7ee5ded6878874bae24564840cfd3859c3b0e99e9002b44c0de26f8"),
        }) Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(read(start, end - start))).ToLowerInvariant());
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(read(0x15f574, 4)));
        byte[] bases = read(0x1bd820, 19);
        for (int i = 0; i < bases.Length; i++) Assert.Equal(2 + bases[i], Rac1PlayerAudioSession.FootstepIndex(i, 0, 0));
        Assert.Equal(0x24140001u, BinaryPrimitives.ReadUInt32LittleEndian(read(0x2d46ec, 4))); // Toad's initialized softness = 1.
        Assert.Equal(0xa2540029u, BinaryPrimitives.ReadUInt32LittleEndian(read(0x2d470c, 4)));
        Assert.Equal(0xa2800029u, BinaryPrimitives.ReadUInt32LittleEndian(read(0x2e0cac, 4))); // Robot's initialized softness = 0.
    }

    [SkippableFact]
    public void RetailPlayerCuesAndFootstepVoicesDecodeFromEachLevelsOwnBank()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(path), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(path!);
        var levels = Rac1DiscIndex.Read(disc).Levels;
        foreach (int id in new[] { 0, 1, 2, 12 })
        {
            var level = levels.Single(level => level.LevelId == id);
            var core = Rac1LevelCore.Open(disc, level);
            var audio = Rac1PlayerAudioAssets.Decode(disc, level, core);
            Assert.Equal([(8, 26), (160, 30), (208, 31)], audio.Animations[7].Cues.Select(cue => ((int)cue.PositionUnits, (int)cue.SoundId)));
            Assert.Equal([(32, 27)], audio.Animations[23].Cues.Select(cue => ((int)cue.PositionUnits, (int)cue.SoundId)));
            Assert.Equal([(48, 36), (352, 30)], audio.Animations[28].Cues.Select(cue => ((int)cue.PositionUnits, (int)cue.SoundId)));
            Assert.Equal([(24, 33), (64, 39), (112, 31)], audio.Animations[16].Cues.Select(cue => ((int)cue.PositionUnits, (int)cue.SoundId)));
            Assert.Equal([(8, 60), (232, 35)], audio.Animations[69].Cues.Select(cue => ((int)cue.PositionUnits, (int)cue.SoundId)));
            foreach (int surface in Enumerable.Range(0, 3))
                foreach (int foot in Enumerable.Range(0, 2))
                {
                    var sound = audio.Footsteps[Rac1PlayerAudioSession.FootstepIndex(id, surface, foot)];
                    Assert.True(sound.Clip.FrameCount > 0);
                    Assert.Contains(sound.Clip.InterleavedPcm16, sample => sample != 0);
                    Assert.False(sound.Loop);
                }
            var body = Rac1CrateAudio.Decode(disc, level, core, new HashSet<int> { 0, 71 });
            foreach (int sound in new[] { 5, 26, 27, 30, 31, 36 }) Assert.True(body[(0, sound)].Clip.FrameCount > 0);
            foreach (int sound in new[] { 2, 14, 18 }) Assert.True(body[(0, sound)].Loop);
            foreach (int sound in new[] { 0, 1, 2 }) Assert.True(body[(71, sound)].Clip.FrameCount > 0);
        }
    }

    [SkippableFact]
    public void OriginalThirdSwingVoiceProgramAdmitsFourAudibleAndFourSilentAlternatives()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(disc).Levels.Single(level => level.LevelId == 0);
        var core = Rac1LevelCore.Open(disc, level);
        var audio = Rac1PlayerAudioAssets.Decode(disc, level, core);
        Assert.Contains(audio.Animations[25].Cues, cue => cue.SoundId == 54);
        var voice = Rac1CrateAudio.Decode(disc, level, core, new HashSet<int> { 0 })[(0, 54)];
        Assert.False(voice.Loop); Assert.NotNull(voice.Alternatives);
        Assert.Equal(8, voice.Alternatives!.Count);
        Assert.Equal(4, voice.Alternatives.Count(clip => clip.FrameCount == 0));
        Assert.Equal(4, voice.Alternatives.Count(clip => clip.FrameCount > 0));
    }

    [Fact]
    public void AcceptedHitUsesFrame3AndFatalHitFollowsItsOwnDeathTimeline()
    {
        var hit = Clip(16, (24, 33), (64, 39), (112, 31)) with { FrameDurations = Enumerable.Repeat(1d / 30d, 18).ToArray() };
        var death = Clip(69, (8, 60), (232, 35)) with { FrameDurations = Enumerable.Repeat(1d / 15d, 25).ToArray() };
        var session = new Rac1PlayerReactionAudioSession(new Dictionary<int, Rac1AnimationSoundClip> { [16] = hit, [69] = death });
        Assert.False(session.NotifyDamage(4, 4));
        Assert.Null(session.Step());
        Assert.True(session.NotifyDamage(4, 3));
        var sounds = Enumerable.Range(0, 60).Select(_ => session.Step()).Where(sound => sound.HasValue).ToArray();
        Assert.Equal(new int?[] { 39, 31 }, sounds); // The earlier sound33 lies before frame3 and must be skipped.
        Assert.True(session.NotifyDamage(1, 0));
        var fatal = Enumerable.Range(0, 150).Select(_ => session.Step()).Where(sound => sound.HasValue).ToArray();
        Assert.Equal(new int?[] { 39, 31, 60, 35 }, fatal);
        session.NotifyDamage(4, 3); session.Reset();
        Assert.All(Enumerable.Range(0, 60), _ => Assert.Null(session.Step()));
    }

    private static Rac1AnimationSoundClip Clip(int sequence, params (ushort Position, ushort Sound)[] cues) => new(
        sequence, null, cues.Select(cue => new Rac1MobyAnimation.TimedSoundCue(
            (uint)(cue.Position << 16) | cue.Sound, cue.Position, cue.Sound)).ToArray(),
        Enumerable.Range(0, 64).Select(frame => frame * 3d).ToArray());

    private static Rac1AnimationFrameClock.Sample Sample(int sequence, int frame, float fraction, bool blending = false) =>
        new(sequence, frame, frame + 1, fraction, blending);
}
