using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Animation;
using OBP.RAC1.Level;
using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1RatchetAvatarTests
{
    [SkippableFact]
    public void Level0_AdmittedAvatarClipsArePinnedEngineIndependentLocalData()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == 0);
        var core = Rac1LevelCore.Open(reader, level);
        var avatar = Rac1RatchetAvatar.Decode(core);

        Assert.Equal(0, avatar.ClassId);
        Assert.Equal(111, avatar.JointCount);
        Assert.Equal(5_583, avatar.Mesh.Positions.Length / 3);
        Assert.Equal(6_856, avatar.Mesh.Indices.Length / 3);
        Assert.Equal(5_583 * 2, avatar.Mesh.Uvs.Length);
        Assert.Equal(111, avatar.Skeleton.Count);
        Assert.Equal(11, avatar.AnimationClips.Count);
        Assert.Equal(10, avatar.StandingFrames.Count);
        Assert.Equal(7.5f, avatar.FramesPerSecond);

        AssertClip(avatar, Rac1RatchetAvatar.StandingSequenceId, 10, 7.5f, variable: false);
        AssertClip(avatar, Rac1RatchetAvatar.LocomotionStartSequenceId, 33, 15f, variable: false);
        AssertClip(avatar, Rac1RatchetAvatar.SustainedLocomotionSequenceId, 23, 30f, variable: false);
        AssertClip(avatar, Rac1RatchetAvatar.LocomotionStopVariantASequenceId, 13, 15f, variable: false);
        AssertClip(avatar, Rac1RatchetAvatar.LocomotionStopVariantBSequenceId, 13, 15f, variable: false);
        AssertClip(avatar, Rac1RatchetAvatar.StationaryJumpSequenceId, 29, null, variable: true);
        AssertClip(avatar, Rac1RatchetAvatar.MovingJumpSequenceId, 16, null, variable: true);
        AssertClip(avatar, Rac1RatchetAvatar.CrouchSequenceId, 15, 15f, variable: false);
        AssertClip(avatar, Rac1RatchetAvatar.CrouchTurnRightSequenceId, 7, 15f, variable: false);
        AssertClip(avatar, Rac1RatchetAvatar.CrouchTurnLeftSequenceId, 7, 15f, variable: false);
        AssertClip(avatar, Rac1RatchetAvatar.WrenchAttackSequenceId, 21, null, variable: true);

        var cls = Rac1StaticClasses.Read(core).Mobies[0];
        var sequences = Rac1MobyAnimation.ReadRatchetSequences(
            core.Assets,
            core.Index,
            core.Header.RatchetSequencesOffset,
            cls.JointCount);
        foreach (var clip in avatar.AnimationClips)
        {
            var source = Assert.IsType<Rac1MobyAnimation.Sequence>(sequences[clip.SequenceId].Value);
            Assert.Equal(source.Frames.Count, clip.FrameDurationsSeconds.Count);
            for (int frame = 0; frame < source.Frames.Count; frame++)
            {
                float rate = source.ConstantTransitionRateRaw != 0
                    ? source.ConstantTransitionRate
                    : source.Frames[frame].TransitionRate;
                double expected = 1d / (rate * Rac1RatchetAvatar.NtscUpdateHz);
                Assert.Equal(expected, clip.FrameDurationsSeconds[frame], 12);
            }
        }

        Assert.Equal(
            new[] { 130, 404, 966, 5_356 },
            avatar.Surfaces.Select(s => s.Indices.Length / 3).Order().ToArray());
        Assert.Equal(6_856, avatar.Surfaces.Sum(s => s.Indices.Length / 3));
        Assert.Equal(new[] { 0, 1, 2, 3 }, avatar.TextureIds);
        Assert.All(avatar.Surfaces, surface =>
            Assert.All(surface.Indices, index => Assert.InRange(index, 0, 5_582)));
        Assert.All(avatar.AnimationClips.SelectMany(clip => clip.LocalFrames), frame =>
        {
            Assert.Equal(5_583 * 3, frame.Length);
            Assert.All(frame, value => Assert.True(double.IsFinite(value)));
        });

        Assert.Equal(new Rac1RatchetAvatar.LocalPoint(0, 0, 0), avatar.Origin);
        Assert.Equal(-0.7915445693943184, avatar.RestBounds.Min.X, 12);
        Assert.Equal(-0.9060465186194051, avatar.RestBounds.Min.Y, 12);
        Assert.Equal(0.001708984316792339, avatar.RestBounds.Min.Z, 12);
        Assert.Equal(0.3121744685340673, avatar.RestBounds.Max.X, 12);
        Assert.Equal(0.9060465186194051, avatar.RestBounds.Max.Y, 12);
        Assert.Equal(1.415893506462453, avatar.RestBounds.Max.Z, 12);
        Assert.Equal(Rac1RatchetAvatar.AxisDirection.PositiveX, avatar.RightAxis);
        Assert.Equal(Rac1RatchetAvatar.AxisDirection.PositiveY, avatar.ForwardAxis);
        Assert.Equal(Rac1RatchetAvatar.AxisDirection.PositiveZ, avatar.UpAxis);
        Assert.Equal(-0.03446032626043135, avatar.BaseHeightZ, 12);
        Assert.Equal(-0.4128220525325828, avatar.StandingBounds.Min.X, 12);
        Assert.Equal(-0.40748244836872116, avatar.StandingBounds.Min.Y, 12);
        Assert.Equal(-0.03446032626043135, avatar.StandingBounds.Min.Z, 12);
        Assert.Equal(0.36215315698108996, avatar.StandingBounds.Max.X, 12);
        Assert.Equal(0.3055202193797982, avatar.StandingBounds.Max.Y, 12);
        Assert.Equal(1.3954319384256932, avatar.StandingBounds.Max.Z, 12);
        Assert.Equal(0.7749752095136728, avatar.StandingBounds.Width, 12);
        Assert.Equal(0.7130026677485193, avatar.StandingBounds.Depth, 12);
        Assert.Equal(1.4298922646861245, avatar.StandingBounds.Height, 12);
    }

    [SkippableFact]
    public void Level0_LocalStandingFramesMapExactlyToExistingWorldRatchetPath()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == 0);
        var core = Rac1LevelCore.Open(reader, level);
        var avatar = Rac1RatchetAvatar.Decode(core);
        var gameplay = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(reader, level));
        var instance = Assert.Single(gameplay.MobyInstances, i => i.OClass == 0);
        Assert.Equal(0, instance.Index);

        var world = Rac1WorldImport.Build(reader, 0);
        var worldRatchet = world.AnimatedMeshes!
            .Where(mesh => mesh.Name.StartsWith("ratchet_", StringComparison.Ordinal))
            .OrderBy(mesh => mesh.TextureId)
            .ToArray();
        var localSurfaces = avatar.Surfaces.OrderBy(surface => surface.TextureId).ToArray();
        Assert.Equal(localSurfaces.Length, worldRatchet.Length);
        Assert.Equal(4, worldRatchet.Length);

        for (int surface = 0; surface < worldRatchet.Length; surface++)
        {
            Assert.Equal(localSurfaces[surface].TextureId, worldRatchet[surface].TextureId);
            Assert.Equal(localSurfaces[surface].Indices, worldRatchet[surface].Indices);
            Assert.Equal(avatar.Mesh.Uvs, worldRatchet[surface].Uvs);
        }

        for (int frameIndex = 0; frameIndex < avatar.StandingFrames.Count; frameIndex++)
        {
            var local = avatar.StandingFrames[frameIndex];
            var placed = worldRatchet[0].Frames[frameIndex];
            Assert.False(local.SequenceEqual(placed));
            for (int i = 0; i < local.Length; i += 3)
            {
                var point = Rac1Instances.TransformMobyPoint(
                    instance, local[i], local[i + 1], local[i + 2]);
                Assert.Equal(R2(point.X), placed[i]);
                Assert.Equal(R2(point.Z), placed[i + 1]);
                Assert.Equal(R2(point.Y), placed[i + 2]);
            }
        }
    }

    private static void AssertClip(
        Rac1RatchetAvatar.Asset avatar,
        int sequenceId,
        int frameCount,
        float? fps,
        bool variable)
    {
        var clip = avatar.Clip(sequenceId);
        Assert.Equal(frameCount, clip.FrameCount);
        Assert.Equal(variable, clip.HasVariableTiming);
        Assert.Equal(fps, clip.ConstantFramesPerSecond);
        Assert.Equal(frameCount, clip.FrameDurationsSeconds.Count);
        Assert.All(clip.FrameDurationsSeconds, duration => Assert.True(duration > 0 && double.IsFinite(duration)));
    }

    private static double R2(double value) => Math.Floor(value * 100 + 0.5) / 100 + 0.0;
}
