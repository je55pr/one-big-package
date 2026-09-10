using OBP.IO;
using OBP.PS2.Textures;
using OBP.RAC1.Level;
using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class Rac1PlayerAvatarProviderTests
{
    [Fact]
    public void RoutingIdentityIsStableAndSourceGameNeutral()
    {
        IPlayerAvatarProvider provider = Rac1PlayerAvatarProvider.Instance;

        Assert.Equal("rac1", provider.SourceGame);
        Assert.Equal("rac1-ntscu-original", provider.BuildId);
        Assert.True(provider.CanLoad(Rac1PlayerAvatarProvider.RatchetAvatarId));
        Assert.False(provider.CanLoad("clank"));
    }

    [SkippableFact]
    public void RetailProviderCarriesCompleteLocalSpaceRatchetPresentation()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");

        var avatar = Rac1PlayerAvatarProvider.Instance.Load(
            iso!, Rac1PlayerAvatarProvider.RatchetAvatarId);

        Assert.Equal("rac1", avatar.Identity.SourceGame);
        Assert.Equal("rac1-ntscu-original", avatar.Identity.BuildId);
        Assert.Equal("ratchet", avatar.Identity.AvatarId);
        Assert.Equal("rac1:ratchet:moby-class-0", avatar.Identity.ModelId);
        Assert.Equal(11, avatar.AnimationClips.Count);
        Assert.Equal(10, avatar.LocalAnimationFrames.Count);
        Assert.Equal(7.5f, avatar.FramesPerSecond);
        Assert.Equal(5_583, avatar.VertexCount);
        Assert.Null(avatar.Skeleton);

        Assert.Equal(
            new[]
            {
                "standing",
                "locomotion-start",
                "sustained-locomotion",
                "locomotion-stop-a",
                "locomotion-stop-b",
                "stationary-jump",
                "moving-jump",
                "crouch",
                "crouch-turn-right",
                "crouch-turn-left",
                "primary-attack",
            },
            avatar.AnimationClips.Select(clip => clip.Id).ToArray());
        Assert.Equal(2, avatar.AnimationClips.Count(
            clip => clip.Role == PlayerAvatarAnimationRole.LocomotionStopVariant));
        Assert.True(avatar.RequiredAnimationClip(PlayerAvatarAnimationRole.StationaryJump).HasVariableTiming);
        Assert.True(avatar.RequiredAnimationClip(PlayerAvatarAnimationRole.MovingJump).HasVariableTiming);
        Assert.True(avatar.RequiredAnimationClip(PlayerAvatarAnimationRole.PrimaryAttack).HasVariableTiming);
        Assert.Equal(30f, avatar.RequiredAnimationClip(
            PlayerAvatarAnimationRole.SustainedLocomotion).ConstantFramesPerSecond);

        Assert.Equal(new[] { 0, 1, 2, 3 }, avatar.Surfaces.Select(s => s.TextureId).ToArray());
        Assert.Equal(new[] { 0, 1, 2, 3 }, avatar.Textures.Select(t => t.TextureId).ToArray());
        Assert.Equal(6_856, avatar.Surfaces.Sum(surface => surface.TriangleCount));
        Assert.All(avatar.Surfaces, surface =>
        {
            Assert.Equal(5_583 * 2, surface.Uvs.Length);
            Assert.NotEmpty(surface.Indices);
            Assert.Contains(avatar.Textures, texture => texture.MaterialId == surface.MaterialId);
        });
        Assert.All(avatar.Textures, texture =>
        {
            Assert.True(texture.Width > 0);
            Assert.True(texture.Height > 0);
            Assert.Equal(texture.Width * texture.Height * 4, texture.Rgba.Length);
        });

        Assert.Equal(new PlayerAvatarPoint(0, 0, 0), avatar.Origin);
        Assert.Equal(PlayerAvatarAxisDirection.PositiveX, avatar.Axes.Right);
        Assert.Equal(PlayerAvatarAxisDirection.PositiveY, avatar.Axes.Forward);
        Assert.Equal(PlayerAvatarAxisDirection.PositiveZ, avatar.Axes.Up);
        Assert.Equal(-0.03446032626043135, avatar.BaseHeight, 12);
        Assert.All(avatar.AnimationClips.SelectMany(clip => clip.LocalFrames), frame =>
        {
            Assert.Equal(5_583 * 3, frame.Length);
            Assert.All(frame, value => Assert.True(double.IsFinite(value)));
        });

        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(level =>
            level.LevelId == Rac1PlayerAvatarProvider.CanonicalAvatarLevelId);
        var native = Rac1RatchetAvatar.Decode(Rac1LevelCore.Open(reader, level));
        AssertNeutralClip(avatar, "standing", native.Clip(Rac1RatchetAvatar.StandingSequenceId));
        AssertNeutralClip(avatar, "locomotion-start", native.Clip(Rac1RatchetAvatar.LocomotionStartSequenceId));
        AssertNeutralClip(avatar, "sustained-locomotion", native.Clip(Rac1RatchetAvatar.SustainedLocomotionSequenceId));
        AssertNeutralClip(avatar, "locomotion-stop-a", native.Clip(Rac1RatchetAvatar.LocomotionStopVariantASequenceId));
        AssertNeutralClip(avatar, "locomotion-stop-b", native.Clip(Rac1RatchetAvatar.LocomotionStopVariantBSequenceId));
        AssertNeutralClip(avatar, "stationary-jump", native.Clip(Rac1RatchetAvatar.StationaryJumpSequenceId));
        AssertNeutralClip(avatar, "moving-jump", native.Clip(Rac1RatchetAvatar.MovingJumpSequenceId));
        AssertNeutralClip(avatar, "crouch", native.Clip(Rac1RatchetAvatar.CrouchSequenceId));
        AssertNeutralClip(avatar, "crouch-turn-right", native.Clip(Rac1RatchetAvatar.CrouchTurnRightSequenceId));
        AssertNeutralClip(avatar, "crouch-turn-left", native.Clip(Rac1RatchetAvatar.CrouchTurnLeftSequenceId));
        AssertNeutralClip(avatar, "primary-attack", native.Clip(Rac1RatchetAvatar.WrenchAttackSequenceId));
    }

    [SkippableFact]
    public void RetailCanonicalLevelIsSafeCarrierForRatchetModelAndTextures()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");

        var canonical = Rac1PlayerAvatarProvider.Instance.Load(
            iso!, Rac1PlayerAvatarProvider.RatchetAvatarId);
        using var reader = new FileRandomAccessReader(iso!);
        var catalogue = Rac1DiscIndex.Read(reader);

        foreach (var level in catalogue.Levels.OrderBy(level => level.LevelId))
        {
            var core = Rac1LevelCore.Open(reader, level);
            var native = Rac1RatchetAvatar.Decode(core);
            Assert.Equal(canonical.Surfaces.Select(s => s.TextureId), native.TextureIds);
            Assert.Equal(canonical.LocalAnimationFrames.Count, native.StandingFrames.Count);
            for (int frame = 0; frame < native.StandingFrames.Count; frame++)
                Assert.Equal(canonical.LocalAnimationFrames[frame], native.StandingFrames[frame]);

            var decoded = RcLevelTextureTable.Read(
                core.Index,
                core.Assets,
                core.GsRam,
                core.Header.TexturesBaseOffset,
                new RcLevelTextureTable.Range(
                    core.Header.MobyTextures.Count,
                    core.Header.MobyTextures.Offset))
                .ToDictionary(texture => texture.Index);

            foreach (var expected in canonical.Textures)
            {
                Assert.True(decoded.TryGetValue(expected.TextureId, out var actual),
                    $"Level {level.LevelId} lacks Ratchet texture {expected.TextureId}.");
                Assert.Equal(expected.Width, actual!.Width);
                Assert.Equal(expected.Height, actual.Height);
                Assert.Equal(expected.Rgba, actual.Rgba);
            }
        }
    }

    private static void AssertNeutralClip(
        PlayerAvatar avatar,
        string clipId,
        Rac1RatchetAvatar.AnimationClip native)
    {
        var clip = Assert.Single(avatar.AnimationClips, clip => clip.Id == clipId);
        Assert.Equal(native.LocalFrames.Count, clip.LocalFrames.Count);
        Assert.Equal(native.FrameDurationsSeconds, clip.FrameDurationsSeconds);
        for (int frame = 0; frame < native.LocalFrames.Count; frame++)
            Assert.Equal(native.LocalFrames[frame], clip.LocalFrames[frame]);
    }
}
