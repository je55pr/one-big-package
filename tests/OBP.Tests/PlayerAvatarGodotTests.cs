using OBP.Core;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Player;
using Xunit;
using static OBP.Tests.PlayerAvatarTestFixtures;

namespace OBP.Tests;

/// <summary>
/// Godot view adapter: native-to-Godot frame/axis conversion, facing and the presentation sink seam.
/// </summary>
public sealed class PlayerAvatarGodotTests
{
    [Fact]
    public void RecoveredRuntimeSpawnAddsOnlyHostGroundingLiftAndSceneMirror()
    {
        var spawn = new RuntimeSpawn(
            132.09,
            31.4266167,
            115.48,
            0.6627014875);

        RuntimeSpawnScenePose pose = RuntimeSpawnSceneAdapter.ToScenePose(spawn);

        Assert.Equal((float)-spawn.X, pose.Position.X);
        Assert.Equal((float)(spawn.Y + RuntimeSpawnSceneAdapter.GroundingLift), pose.Position.Y);
        Assert.Equal((float)spawn.Z, pose.Position.Z);
        Assert.Equal((float)-spawn.Yaw, pose.SceneYaw);
    }

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
    public void ConvertFrameIntoReusesBufferAndPreservesAxisMapping()
    {
        double[] source = [1, 2, 3, -4, 5, 6];
        var converted = new global::Godot.Vector3[2];

        PlayerAvatarView.ConvertFrameInto(source, Rac1Axes, 0.5, true, converted);

        Assert.Equal(new global::Godot.Vector3(1, 2.5f, -2), converted[0]);
        Assert.Equal(new global::Godot.Vector3(-4, 5.5f, -5), converted[1]);
        Assert.Equal(PlayerAvatarView.ConvertFrame(source, Rac1Axes, 0.5, true), converted);

        double[] next = [7, 8, 9, 10, 11, 12];
        PlayerAvatarView.ConvertFrameInto(next, Rac1Axes, 0.5, true, converted);
        Assert.Equal(new global::Godot.Vector3(7, 8.5f, -8), converted[0]);
        Assert.Equal(new global::Godot.Vector3(10, 11.5f, -11), converted[1]);
        Assert.Throws<InvalidDataException>(() =>
            PlayerAvatarView.ConvertFrameInto(source, Rac1Axes, 0, false, new global::Godot.Vector3[1]));
    }

    [Theory]
    [InlineData(0d, 1d)]
    [InlineData(1d, 0d)]
    [InlineData(-1d, 0d)]
    [InlineData(0d, -1d)]
    [InlineData(1d, 1d)]
    [InlineData(-1d, 1d)]
    [InlineData(1d, -1d)]
    [InlineData(-1d, -1d)]
    public void Rac1VisibleMeshFacingMatchesNativeFacingAndMovement(double inputX, double inputY)
    {
        double nativeYaw = Rac1RatchetYawController.BuildMovementTarget(inputX, inputY, controlYaw: 0d);
        float sceneYaw = PlayerAvatarFacing.NativeZUpYawToGodotSceneYaw(nativeYaw);
        // Ratchet's reconstructed neutral mesh visibly faces local +X.
        var visualForward = (new global::Godot.Basis(global::Godot.Vector3.Up, sceneYaw) *
                             global::Godot.Vector3.Right).Normalized();

        var nativeFacing = new Rac1WrenchCombatController().ResolveFirstSwingFacing(nativeYaw);
        var wrenchForward = PlayerAvatarFacing.NativeZUpPlanarDirectionToGodot(
            nativeFacing.X, nativeFacing.Y).Normalized();

        var movement = new Rac1RatchetMovementController();
        var movementBasis = new PlayerPlanarBasis(0d, -1d, -1d, 0d);
        var nativeBasis = new PlayerPlanarBasis(-1d, 0d, 0d, 1d);
        var step = movement.Step(
            new PlayerControlIntent(inputX, inputY, false, false, false, movementBasis, nativeBasis),
            new PlayerContactFacts(true),
            _ => nativeYaw);
        var movementForward = new global::Godot.Vector3(
            (float)step.PlanarX, 0f, (float)step.PlanarY).Normalized();

        Assert.InRange((visualForward - wrenchForward).Length(), 0f, 0.00001f);
        Assert.InRange((visualForward - movementForward).Length(), 0f, 0.00001f);
    }

    [Fact]
    public void ViewImplementsResolvedAnimationPresentationSinkSeam()
    {
        Assert.True(typeof(IPlayerAnimationPresentationSink).IsAssignableFrom(typeof(PlayerAvatarView)));
    }
}
