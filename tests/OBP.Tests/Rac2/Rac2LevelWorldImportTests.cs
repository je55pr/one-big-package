using OBP.Core;
using OBP.IO;
using OBP.RAC2;
using OBP.RAC2.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using static OBP.Tests.Rac2RetailLevelFixtures;

namespace OBP.Tests;

/// <summary>
/// Assembled <c>Rac2WorldImport</c> results for the retail Oozla (LEVEL1) and Aranos (LEVEL0) worlds. Oozla is
/// imported once per class; each test only reads the shared world. Retail-gated on <c>OBP_GC_ISO</c>.
/// </summary>
public sealed class Rac2LevelWorldImportTests : IClassFixture<Rac2OozlaWorldImportFixture>
{
    private readonly Rac2OozlaWorldImportFixture _oozla;

    public Rac2LevelWorldImportTests(Rac2OozlaWorldImportFixture oozla)
    {
        _oozla = oozla;
    }

    [SkippableFact]
    public void Level1_WorldImportRenderGeometryMatchesTheBaseline()
    {
        var world = OozlaWorld();

        Assert.Equal("Oozla — The Megacorp Outlet", world.DisplayName);

        // Render geometry. tfrags now come from every chunk slot (spatial tiles),
        // not just chunk 0 — chunk 0 alone is only the region around the ship.
        // 2 oc1134 instances are lifted out as animated mobies (below). Native material
        // presentation splits two same-texture Moby surface buckets without adding triangles.
        Assert.Equal(304, world.Meshes.Count);
        // The native packet-cache path recovers additional source geometry;
        // keep the historical witness for OBP_RAC2_LEGACY_MOBY=1.
        bool nativePose = Environment.GetEnvironmentVariable("OBP_RAC2_LEGACY_MOBY") != "1";
        Assert.Equal(nativePose ? 1_950_147 : 1_860_879, world.TotalRenderTriangles);
        Assert.Equal(nativePose ? 23_560 : 21_470, world.TotalDynamicTriangles);
        Assert.Equal(nativePose ? 1_973_707 : 1_882_349,
            world.TotalRenderTriangles + world.TotalDynamicTriangles);
        Assert.Equal(398, world.MaterialCount);

        int Meshes(string kind) => world.Meshes.Count(m => m.AssetKind == kind);
        int Tris(string kind) => world.Meshes.Where(m => m.AssetKind == kind).Sum(m => m.TriangleCount);

        Assert.Equal((80, 27_384), (Meshes("tfrag"), Tris("tfrag"))); // chunk 0: 77 / 26,192 + chunk 1: 3 / 1,192
        Assert.Equal((79, 674_252), (Meshes("tie"), Tris("tie")));
        Assert.Equal((38, 852_785), (Meshes("shrub"), Tris("shrub")));
        Assert.Equal((101, nativePose ? 393_454 : 304_186),
            (Meshes("moby"), Tris("moby")));
        Assert.Equal((1, 576), (Meshes("moby-marker"), Tris("moby-marker")));
        Assert.Equal((4, 1_694), (Meshes("sky"), Tris("sky")));
        Assert.Equal((1, 2), (Meshes("death-plane"), Tris("death-plane")));
    }

    [SkippableFact]
    public void Level1_WorldImportKeepsBoltCratesAsPerInstanceDynamicObjects()
    {
        var world = OozlaWorld();

        // First production dynamic-Moby slice: all class-500 Bolt Crates keep
        // per-instance identity/render/state instead of contributing to the
        // welded moby mesh. Static+dynamic triangle accounting is unchanged.
        Assert.NotNull(world.DynamicObjects);
        var dynamicCrates = world.DynamicObjects!.Where(o => o.NativeClassId == 500).ToArray();
        Assert.Equal(190, dynamicCrates.Length);
        Assert.All(dynamicCrates, o =>
        {
            Assert.Equal(ObpSourceGame.Rac2, o.SourceGame);
            Assert.Equal($"moby:{o.NativeClassId}", o.ModelRef);
            Assert.StartsWith("level:1:moby:", o.InteractionId);
            Assert.Equal(16, o.Transform.Matrix.Length);
            Assert.NotEmpty(o.Meshes);
            Assert.Contains(o.NativePayloads!, p => p.Format == "rac2-moby-instance-0x88" && p.Data.Length == 0x88);
            Assert.Contains(o.NativePayloads!, p => p.Format == "rac2-pvar" && p.Data.Length == 0x110);
        });

        var showcaseRuntimeCrate = Assert.Single(dynamicCrates, o => o.InstanceIndex == 31);
        var showcaseLifecycle = Rac2Class500Lifecycle.ApplyRecoveredBreak(
            showcaseRuntimeCrate, RuntimeEntityState.FromAuthored(showcaseRuntimeCrate));
        Assert.Equal(73, showcaseLifecycle.Authored.Uid);
        Assert.Equal(13, showcaseLifecycle.Authored.AuthoredBolts);
        Assert.Equal((byte?)0, showcaseLifecycle.Authored.PvarC8);
        Assert.Equal(Rac2Class500PostBreakRoute.Deactivate, showcaseLifecycle.Route);
        Assert.Equal(RuntimeEntityPresence.Inactive, showcaseLifecycle.EntityState.Presentation.Presence);
    }

    [SkippableFact]
    public void Level1_WorldImportCollisionMatchesTheBaseline()
    {
        var world = OozlaWorld();

        // Collision — now carries the actual decoded geometry (OBP Y-up), the
        // surface the debug capsule stands on.
        Assert.Equal(2, world.CollisionMeshes.Count);
        var coll0 = world.CollisionMeshes[0];
        var coll1 = world.CollisionMeshes[1];
        Assert.Equal((22_404, 308_108, 368_937), (coll0.Octants, coll0.Triangles, coll0.VertexCount));
        Assert.Equal((4_210, 20_815, 33_840), (coll1.Octants, coll1.Triangles, coll1.VertexCount));
        Assert.Equal(328_923, world.CollisionMeshes.Sum(c => c.Triangles));
        Assert.All(world.CollisionMeshes, c =>
        {
            Assert.Equal(c.Triangles * 3, c.Indices.Length);
            Assert.Equal(c.Triangles, c.TriangleMaterialIds.Length);
            Assert.All(c.Indices, i => Assert.InRange(i, 0, c.VertexCount - 1));
        });
        // Collision vertices lie within the world bounds (Y-up mapping applied).
        for (int i = 0; i < coll0.Positions.Length; i += 3)
        {
            Assert.InRange(coll0.Positions[i + 1], world.Bounds.Min.Y - 1, world.Bounds.Max.Y + 1);
        }
    }

    [SkippableFact]
    public void Level1_WorldImportBoundsEnvironmentAndShipMatchTheBaseline()
    {
        var world = OozlaWorld();

        // World bounds (OBP Y-up), unrounded — grown from tfrag + tie + shrub + collision.
        Assert.Equal(2.4044196883506572, world.Bounds.Min.X, 9);
        Assert.Equal(50.109375, world.Bounds.Min.Y, 9);
        Assert.Equal(204.95078454775668, world.Bounds.Min.Z, 9);
        Assert.Equal(592.5514620817921, world.Bounds.Max.X, 9);
        Assert.Equal(163.1513671875, world.Bounds.Max.Y, 9);
        Assert.Equal(818.3103401622309, world.Bounds.Max.Z, 9);

        // Environment.
        Assert.NotNull(world.Environment);
        Assert.Equal(0f, world.Environment!.DeathHeight);
        Assert.False(world.Environment.IsSphericalWorld);
        Assert.Equal((10 / 255.0, 40 / 255.0, 30 / 255.0), world.Environment.FogColour);

        // Ship spawn — where the debug capsule appears (native (278, 411, 115) -> OBP (278, 115, 411)).
        Assert.NotNull(world.Ship);
        Assert.Equal(278.04974, world.Ship!.X, 3);
        Assert.Equal(115.29899, world.Ship.Y, 3);
        Assert.Equal(411.41284, world.Ship.Z, 3);
        Assert.Equal(1.78391385, world.Ship.Yaw, 5);
        Assert.InRange(world.Ship.Y, world.Bounds.Min.Y, world.Bounds.Max.Y);
    }

    [SkippableFact]
    public void Level1_WorldImportTfragsCarryNativeVertexColours()
    {
        var world = OozlaWorld();

        // Every tfrag mesh carries the native baked per-vertex RGBA (the level's static
        // lighting / AO) — one float4 per vertex, channels in 0..1. Oozla's authored
        // fourth byte is uniformly 0x80, preserved as 128/255 rather than forced opaque.
        var tfrags = world.Meshes.Where(m => m.AssetKind == "tfrag").ToArray();
        Assert.NotEmpty(tfrags);
        foreach (var m in tfrags)
        {
            Assert.NotEmpty(m.Positions);
            Assert.Equal(0, m.Positions.Length % 3);
            Assert.NotNull(m.Colors);
            Assert.Equal(m.Positions.Length / 3 * 4, m.Colors!.Length);
            for (int i = 0; i < m.Colors.Length; i += 4)
            {
                Assert.InRange(m.Colors[i], 0f, 1f);
                Assert.Equal(128 / 255f, m.Colors[i + 3]);
            }
        }
    }

    [SkippableFact]
    public void Level1_WorldImportAnimatesTheSingleJointSpinnerMobies()
    {
        var world = OozlaWorld();

        bool nativePose = Environment.GetEnvironmentVariable("OBP_RAC2_LEGACY_MOBY") != "1";

        // Animated mobies — retail MobySequence playback. Oozla places 2
        // instances of the single-joint spinner oc1134 (170-frame sequence).
        Assert.NotNull(world.AnimatedMeshes);
        Assert.Equal(2, world.AnimatedMeshes!.Count);
        foreach (var am in world.AnimatedMeshes)
        {
            Assert.Equal("moby", am.AssetKind);
            Assert.Equal(170, am.Frames.Count);
            Assert.Equal(nativePose ? 94 : 91, am.TriangleCount);
            Assert.NotEmpty(am.Frames[0]);
            Assert.Equal(0, am.Frames[0].Length % 3);
            Assert.All(am.Indices, index => Assert.InRange(index, 0, am.VertexCount - 1));
            Assert.All(am.Frames, f => Assert.Equal(am.VertexCount * 3, f.Length));
            Assert.Equal(am.VertexCount * 4, am.Colors.Length);
        }
    }

    [SkippableFact]
    public void Level1_WorldImportTexturesResolveForEveryTexturedMesh()
    {
        var world = OozlaWorld();

        // Textures — the Checkpoint E render inputs, keyed (AssetKind, TextureId).
        Assert.Equal(77 + 79 + 38 + 199 + 3, world.Textures.Count);
        Assert.Equal(77, world.Textures.Count(t => t.AssetKind == "tfrag"));
        Assert.Equal(3, world.Textures.Count(t => t.AssetKind == "sky"));
        Assert.All(world.Textures, t => Assert.Equal(t.Width * t.Height * 4, t.Rgba.Length));
        // Every textured render mesh resolves to a decoded texture.
        var texKeys = world.Textures.Select(t => (t.AssetKind, t.TextureId)).ToHashSet();
        var texturedMeshes = world.Meshes.Where(m => m.AssetKind is "tfrag" or "tie" or "shrub" or "moby" && m.TextureId >= 0).ToArray();
        Assert.NotEmpty(texturedMeshes);
        foreach (var m in texturedMeshes)
        {
            Assert.Contains((m.AssetKind, m.TextureId), texKeys);
        }
    }

    [SkippableFact]
    public void Level0_AranosWorldImportMatchesTheBaseline()
    {
        // LEVEL0 is un-chunked (lumps 0-3 only): tfrags + collision come from the
        // level core, not chunk lumps. Exercises that path of GcWorldImport.
        var iso = RequireIso();

        using var reader = new FileRandomAccessReader(iso);
        var world = Rac2WorldImport.Build(reader, 0);

        int Tris(string kind) => world.Meshes.Where(m => m.AssetKind == kind).Sum(m => m.TriangleCount);
        Assert.Equal(262, world.Meshes.Count);
        Assert.Equal(33_710, Tris("tfrag"));
        Assert.Equal(771_104, Tris("tie"));
        Assert.Equal(71_334, Tris("shrub"));
        // oc2602 instances (200-frame spin) are lifted out as animated mobies;
        // class 500 crates, opening lift/doors and Aranos class-2827 MSR I hostiles
        // are preserved separately as per-instance dynamic objects.
        bool nativePose = Environment.GetEnvironmentVariable("OBP_RAC2_LEGACY_MOBY") != "1";
        Assert.Equal(nativePose ? 167_941 : 138_009, Tris("moby"));
        Assert.Equal(nativePose ? 100_838 : 91_651, world.TotalDynamicTriangles);
        Assert.Equal(43, world.DynamicObjects!.Count(o => o.NativeClassId == 500));
        Assert.Single(world.DynamicObjects!, o => o.NativeClassId == Rac2AranosOpeningLiftSession.NativeClassId);
        Assert.Equal(2, world.DynamicObjects!.Count(o => o.NativeClassId == Rac2AranosOpeningDoorSession.NativeClassId));
        Assert.Equal(31, world.DynamicObjects!.Count(o => o.NativeClassId == 2827));

        var openingDoor = Assert.Single(world.DynamicObjects!, o =>
            o.NativeClassId == Rac2AranosOpeningDoorSession.NativeClassId &&
            o.InstanceIndex == Rac2AranosOpeningDoorSession.OpeningInstanceIndex);
        var doorClip = Assert.Single(openingDoor.Animations!.Clips);
        Assert.Equal(RuntimeObjectAnimationRole.Reaction, doorClip.Role);
        Assert.Equal(31, doorClip.FrameCount);
        Assert.Equal(30f, doorClip.ConstantFramesPerSecond);
        Assert.Equal(6, doorClip.Surfaces.Count);

        Assert.NotEmpty(world.AnimatedMeshes!);
        Assert.All(world.AnimatedMeshes!, a => Assert.Equal(200, a.Frames.Count));

        Assert.Single(world.CollisionMeshes);
        Assert.Equal((12_079, 81_113), (world.CollisionMeshes[0].Octants, world.CollisionMeshes[0].Triangles));
        Assert.Equal(world.CollisionMeshes[0].Triangles * 3, world.CollisionMeshes[0].Indices.Length);

        Assert.NotNull(world.Environment);
        Assert.Equal(0f, world.Environment!.DeathHeight);

        // LEVEL0's settings ship tuple is the repeated native default (20,20,20,0),
        // not Ratchet's opening placement. The unique authored class-0 Moby at
        // gameplay instance 0 is the player entry: native (247,194,49.89), yaw pi/2.
        Assert.NotNull(world.Ship);
        Assert.Equal((20d, 20d, 20d, 0d),
            (world.Ship!.X, world.Ship.Y, world.Ship.Z, world.Ship.Yaw));
        Assert.NotNull(world.PlayerStart);
        Assert.Equal(247d, world.PlayerStart!.X, 6);
        Assert.Equal(49.89d, world.PlayerStart.Y, 5);
        Assert.Equal(194d, world.PlayerStart.Z, 6);
        Assert.Equal(Math.PI / 2d, world.PlayerStart.Yaw, 6);
        Assert.Same(world.PlayerStart, world.PreferredPlayerStart);
    }

    private RuntimeWorld OozlaWorld()
    {
        Skip.If(_oozla.World is null, "OBP_GC_ISO not set");
        return _oozla.World!;
    }
}
