using Godot;
using OBP.Godot;
using OBP.RAC3;
using OBP.RAC3.Geometry;

namespace OBP.Tests;

public sealed class UyaSkyPresentationTests
{
    [Fact]
    public void NativeAngularVelocityBecomesTargetedObpShellSpin()
    {
        var shell = Shell([0, 0, 5]);
        var spin = UyaSkyPresentation.SpinFor(shell, 3);
        Assert.NotNull(spin);
        Assert.Equal("uya-sky-shell-3", spin!.TargetGroup);
        Assert.Equal(0, spin.Rate.X);
        Assert.Equal(-5 * 60.0 * 2.0 * Math.PI / 32768.0, spin.Rate.Y, 12);
        Assert.Equal(0, spin.Rate.Z);
    }

    [Fact]
    public void ZeroVelocityDeclaresNoSyntheticMotion() =>
        Assert.Null(UyaSkyPresentation.SpinFor(Shell([0, 0, 0]), 0));

    [Fact]
    public void RotationAxisUsesAxialMirrorTransform() =>
        Assert.Equal(new Vector3(1, -2, -3), RuntimeWorldScene.ToSceneRotationAxis(1, 2, 3));

    private static UyaSky.Shell Shell(short[] velocity) => new(
        Textured: true,
        Bloom: false,
        RotationRaw: [0, 0, 0],
        AngularVelocityRaw: velocity,
        Positions: [],
        Uvs: [],
        Alpha: [],
        Indices: [],
        TriangleTextureIds: [],
        ClusterCount: 0);
}
