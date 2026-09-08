using OBP.Core.Math;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

/// <summary>
/// The engine-independent readout for a picked thing in a loaded world. Native
/// payload bytes are surfaced as (format, length) — never decoded.
/// </summary>
public class WorldObjectDescriptorTests
{
    private static RuntimeWorld World(
        System.Collections.Generic.IReadOnlyList<RuntimeMesh>? meshes = null,
        System.Collections.Generic.IReadOnlyList<RuntimeTexture>? textures = null,
        System.Collections.Generic.IReadOnlyList<RuntimeDynamicObject>? dynamics = null,
        System.Collections.Generic.IReadOnlyList<RuntimeAnimatedMesh>? animated = null) =>
        new(
            Game: "rac2",
            BuildId: "rac2-ntscu-v1.01",
            LevelId: 1,
            PlanetName: "Oozla",
            LocationName: "The Megacorp Outlet",
            Meshes: meshes ?? [],
            Textures: textures ?? [],
            MaterialCount: 0,
            CollisionMeshes: [],
            Bounds: new ObpBounds(Vec3.Zero, new Vec3(10, 10, 10)),
            Environment: null,
            Ship: null,
            DynamicObjects: dynamics,
            AnimatedMeshes: animated);

    private static double[] Trs(double tx, double ty, double tz, double scale)
    {
        // column-major: scaled identity rotation + translation
        return new double[]
        {
            scale, 0, 0, 0,
            0, scale, 0, 0,
            0, 0, scale, 0,
            tx, ty, tz, 1,
        };
    }

    [Fact]
    public void WeldedStaticHit_ReportsKindTextureAndProvenance_NoNativeIdentity()
    {
        var world = World(
            meshes: [new RuntimeMesh("tfrag", 7, new double[9], new float[6], new[] { 0, 1, 2 }, new float[12])],
            textures: [new RuntimeTexture("tfrag", 7, 64, 32, new byte[64 * 32 * 4])]);

        var d = WorldObjectDescriptorBuilder.Build(world, new WorldHit("tfrag", 7, new Vec3(1, 2, 3), TriangleIndex: 4));

        Assert.Equal("welded tfrag", d.Category);
        Assert.Equal(7, d.TextureId);
        Assert.Equal(64, d.TextureWidth);
        Assert.Equal(32, d.TextureHeight);
        Assert.True(d.BackFaceCulled);            // tfrag
        Assert.Null(d.NativeClassId);
        Assert.Empty(d.Payloads);
        Assert.Null(d.Animation);
        Assert.Equal("rac2-ntscu-v1.01", d.BuildId);
        Assert.Equal("Oozla — The Megacorp Outlet", d.WorldName);
        Assert.Equal(4, d.TriangleIndex);
    }

    [Fact]
    public void DynamicObjectHit_SurfacesNativeIdentity_TransformAndPayloadSizeOnly()
    {
        var pvar = new byte[220];
        var obj = new RuntimeDynamicObject(
            SourceGame: "rac2",
            NativeClassId: 500,
            InstanceIndex: 12,
            NativeUid: 0x41A,
            ModelRef: "moby:500",
            InteractionId: "rac2:crate:500#12",
            Transform: new RuntimeObjectTransform(Trs(100, 5, -30, 2.0)),
            Meshes: [new RuntimeObjectMesh("moby", 3,
                new double[] { -1, -1, -1, 1, 1, 1, 0, 2, 0 }, new float[6], new[] { 0, 1, 2 })],
            NativePayloads: [new RuntimeOpaquePayload("rac2-pvar", pvar)]);

        var d = WorldObjectDescriptorBuilder.Build(World(dynamics: [obj]),
            new WorldHit("moby", 3, DynamicObject: obj));

        Assert.Equal("dynamic object", d.Category);
        Assert.Equal(500, d.NativeClassId);
        Assert.Equal(12, d.InstanceIndex);
        Assert.Equal(0x41A, d.NativeUid);
        Assert.Equal("rac2:crate:500#12", d.InteractionId);
        Assert.False(d.BackFaceCulled);           // dynamic objects are 2-sided

        Assert.NotNull(d.Transform);
        Assert.Equal(100, d.Transform!.Value.Translation.X, precision: 5);
        Assert.Equal(2.0, d.Transform.Value.Scale.X, precision: 5);
        Assert.Equal(1.0, d.Transform.Value.Rotation.W, precision: 5); // identity rotation

        Assert.Single(d.Payloads);
        Assert.Equal("rac2-pvar", d.Payloads[0].Format);
        Assert.Equal(220, d.Payloads[0].ByteLength);  // size only — bytes never decoded

        Assert.NotNull(d.LocalBounds);
        Assert.Equal(1, d.TriangleCount);
    }

    [Fact]
    public void AnimatedMeshHit_ReportsPlaybackState()
    {
        var anim = new RuntimeAnimatedMesh("oc1134_t3", "moby", 3,
            new float[6], new[] { 0, 1, 2 }, new float[12],
            Frames: [new double[9], new double[9], new double[9]],
            FramesPerSecond: 20f);

        var d = WorldObjectDescriptorBuilder.Build(World(animated: [anim]),
            new WorldHit("moby", 3, AnimatedMesh: anim,
                AnimationState: new AnimationReadout("oc1134_t3", 20, 3, 1, true, false)));

        Assert.Equal("animated mesh", d.Category);
        Assert.NotNull(d.Animation);
        Assert.Equal("oc1134_t3", d.Animation!.Value.Name);
        Assert.Equal(3, d.Animation.Value.FrameCount);
        Assert.Equal(1, d.Animation.Value.CurrentFrame);
        Assert.True(d.Animation.Value.Playing);
        Assert.False(d.Animation.Value.HasSkeleton);
    }

    [Fact]
    public void Decompose_SeparatesTranslationScaleRotation()
    {
        // 90° about Y, scale 3, translate (10,20,30)  — column-major
        double[] m =
        {
            0, 0, -3, 0,
            0, 3, 0, 0,
            3, 0, 0, 0,
            10, 20, 30, 1,
        };
        var t = WorldObjectDescriptorBuilder.Decompose(m);

        Assert.Equal(new Vec3(10, 20, 30), t.Translation);
        Assert.Equal(3.0, t.Scale.X, precision: 5);
        Assert.Equal(3.0, t.Scale.Y, precision: 5);
        Assert.Equal(3.0, t.Scale.Z, precision: 5);
        double qlen = System.Math.Sqrt((t.Rotation.X * t.Rotation.X) + (t.Rotation.Y * t.Rotation.Y)
            + (t.Rotation.Z * t.Rotation.Z) + (t.Rotation.W * t.Rotation.W));
        Assert.Equal(1.0, qlen, precision: 5);
        Assert.True(System.Math.Abs(t.Rotation.Y) > 0.5); // rotation is about Y
    }

    [Fact]
    public void Rac1WeldedOnlyWorld_HasNoDynamicObjectFields()
    {
        var world = World() with { Game = "rac1", DynamicObjects = null, PlanetName = null, LocationName = null };
        var d = WorldObjectDescriptorBuilder.Build(world, new WorldHit("tie", 2));

        Assert.Equal("welded tie", d.Category);
        Assert.Null(d.NativeClassId);
        Assert.Null(d.Transform);
        Assert.Equal("LEVEL1", d.WorldName);
    }
}
