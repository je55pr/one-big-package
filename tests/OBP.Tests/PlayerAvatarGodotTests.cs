using OBP.Godot.Player;
using OBP.Runtime.Player;
using Xunit;

namespace OBP.Tests;

public sealed class PlayerAvatarGodotTests
{
    private static readonly PlayerAvatarAxes Rac1Axes = new(
        PlayerAvatarAxisDirection.PositiveX,
        PlayerAvatarAxisDirection.PositiveY,
        PlayerAvatarAxisDirection.PositiveZ);

    [Fact]
    public void Rac1LocalAxesMapToGodotRightUpForwardAndAlignBase()
    {
        var mapped = PlayerAvatarView.ToGodotLocal(
            new PlayerAvatarPoint(1, 2, 3), Rac1Axes, baseHeight: 0.5, alignGeometricBase: true);
        Assert.Equal(1f, mapped.X);
        Assert.Equal(2.5f, mapped.Y);
        Assert.Equal(-2f, mapped.Z);
    }

    [Fact]
    public void ConvertFrameKeepsAllVerticesOnOneExplicitLocalTransform()
    {
        double[] source = [1, 2, 3, -4, 5, 6];
        var mapped = PlayerAvatarView.ConvertFrame(source, Rac1Axes);
        Assert.Equal(2, mapped.Length);
        Assert.Equal(new global::Godot.Vector3(1, 3, -2), mapped[0]);
        Assert.Equal(new global::Godot.Vector3(-4, 6, -5), mapped[1]);
    }

    [Fact]
    public void ValidationRequiresMatchingSurfaceTextureAndSharedFrames()
    {
        var avatar = Avatar(textures: []);
        var error = Assert.Throws<InvalidDataException>(() => PlayerAvatarView.Validate(avatar));
        Assert.Contains("matching texture", error.Message);
    }

    [Fact]
    public void FrameSelectionUsesNeutralDeterministicClock()
    {
        var avatar = Avatar();
        Assert.Equal(0, PlayerAvatarView.FrameIndexAt(avatar, 0));
        Assert.Equal(1, PlayerAvatarView.FrameIndexAt(avatar, 0.14));
        Assert.Equal(0, PlayerAvatarView.FrameIndexAt(avatar, 0.27));
    }

    private static PlayerAvatar Avatar(IReadOnlyList<PlayerAvatarTexture>? textures = null)
    {
        var surface = new PlayerAvatarSurface("mat", 0, [0, 0, 1, 0, 0, 1], [0, 1, 2]);
        textures ??= [new PlayerAvatarTexture("mat", 0, 1, 1, [255, 255, 255, 255])];
        return new PlayerAvatar(
            new PlayerAvatarIdentity("test", "test", "avatar", "model"),
            [[0, 0, 0, 1, 0, 0, 0, 1, 0], [0, 0, 0, 2, 0, 0, 0, 2, 0]],
            7.5f,
            [surface],
            textures,
            new PlayerAvatarBounds(new(0, 0, 0), new(1, 1, 1)),
            new PlayerAvatarBounds(new(0, 0, 0), new(2, 2, 2)),
            new PlayerAvatarPoint(0, 0, 0),
            0,
            Rac1Axes);
    }
}
