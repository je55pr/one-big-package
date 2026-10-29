using System.Text.Json;
using OBP.IO;
using OBP.PS2.Geometry;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.RAC3.Level;
using Xunit;

namespace OBP.Tests;

public sealed class Rac3NativeMobyRecoveryTests
{
    [SkippableTheory]
    [InlineData(1, 277_164)]
    [InlineData(8, 271_872)]
    [InlineData(11, 276_014)]
    [InlineData(20, 88_044)]
    [InlineData(50, 75_122)]
    public void NativePacketRecoveryRestoresFacesWithoutChangingWorldIdentity(int table, int expandedTriangles)
    {
        var iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_UYA_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var original = Rac3WorldImport.BuildObserved(reader, table, nativeMobyPoseOverride: false);
        var recovered = Rac3WorldImport.BuildObserved(reader, table, nativeMobyPoseOverride: true);
        var oldWorld = original.World;
        var world = recovered.World;

        Assert.Equal(oldWorld.TotalRenderTriangles, world.TotalRenderTriangles);
        Assert.Equal(oldWorld.TotalCollisionTriangles, world.TotalCollisionTriangles);
        Assert.Equal(original.MobyInstanceCount, recovered.MobyInstanceCount);
        Assert.Equal(original.MobiesWithPvar, recovered.MobiesWithPvar);
        Assert.Equal(oldWorld.DynamicObjects!.Count, world.DynamicObjects!.Count);
        Assert.Equal(oldWorld.DynamicObjects.Select(o => (o.NativeClassId, o.InstanceIndex)),
            world.DynamicObjects.Select(o => (o.NativeClassId, o.InstanceIndex)));
        Assert.Equal(expandedTriangles, world.TotalDynamicTriangles +
            (world.AnimatedMeshes?.Sum(m => m.TriangleCount) ?? 0));
        Assert.True(world.TotalDynamicTriangles > oldWorld.TotalDynamicTriangles);
        Assert.All(world.DynamicObjects.SelectMany(o => o.Meshes).SelectMany(m => m.Positions),
            p => Assert.True(double.IsFinite(p)));
    }

    [SkippableFact]
    public void VeldinSkinnedClassesUseRecoveredPacketsAndNativeFirstKey()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_UYA_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var core = Rac3LevelCore.Open(reader, 1).Core;
        int texCount = Rac3Assets.ReadTextures(core, Rac3Assets.TextureTable.Moby).Count;
        var legacy = Rac3Assets.ReadMobyClasses(core, texCount, nativePoseOverride: false);
        var recovered = Rac3Assets.ReadMobyClasses(core, texCount, nativePoseOverride: true);

        foreach (var (oClass, legacyVertices, recoveredVertices, legacyFaces, recoveredFaces) in
            new[] { (6836, 2988, 3140, 2457, 2692), (6306, 3730, 4048, 3308, 3817),
                    (5821, 805, 1029, 832, 1203), (6886, 461, 486, 375, 407) })
        {
            var oldMesh = legacy.Decoded[oClass].Mesh;
            var mesh = recovered.Decoded[oClass].Mesh;
            Assert.Equal(legacyVertices, oldMesh.Positions.Length / 3);
            Assert.Equal(recoveredVertices, mesh.Positions.Length / 3);
            Assert.Equal(legacyFaces, oldMesh.Indices.Length / 3);
            Assert.Equal(recoveredFaces, mesh.Indices.Length / 3);
            Assert.True(mesh.NativeInitialPoseApplied);
            Assert.True(mesh.SkinStateFullyResolved);
            Assert.Equal(mesh.Positions.Length, mesh.Normals.Length);
            Assert.All(mesh.Positions, p => Assert.True(double.IsFinite(p)));
            Assert.All(mesh.Normals, n => Assert.True(float.IsFinite(n)));
        }
    }

    [SkippableFact]
    public void UnsupportedTable11SkinningRetainsDrawableLegacyClass7158()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_UYA_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var core = Rac3LevelCore.Open(reader, 11).Core;
        int textureCount = Rac3Assets.ReadTextures(core, Rac3Assets.TextureTable.Moby).Count;
        var legacy = Rac3Assets.ReadMobyClasses(core, textureCount, nativePoseOverride: false).Decoded[7158].Mesh;
        var native = Rac3Assets.ReadMobyClasses(core, textureCount, nativePoseOverride: true).Decoded[7158].Mesh;
        Assert.True(legacy.SkinningApplied);
        Assert.True(native.SkinningApplied);
        Assert.Equal(legacy.Indices, native.Indices);
        Assert.Equal(legacy.Positions, native.Positions);
        Assert.False(native.NativeInitialPoseApplied);
    }

    [SkippableFact]
    public void EveryRetailWorldAdmitsRecoveredNativeMobyPacketsWithoutLosingActors()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_UYA_ISO not set");
        var path = Path.Combine(RepoPaths.Root, "research", "generated",
            "rac3-ntscu-original.production-import-census.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        using var reader = new FileRandomAccessReader(iso!);
        foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
        {
            int table = row.GetProperty("tableIndex").GetInt32();
            var result = Rac3WorldImport.BuildObserved(reader, table, nativeMobyPoseOverride: true);
            var world = result.World;
            Assert.Equal(row.GetProperty("worldRenderTriangles").GetInt32(), world.TotalRenderTriangles);
            Assert.Equal(row.GetProperty("collisionTriangles").GetInt32(), world.TotalCollisionTriangles);
            Assert.Equal(row.GetProperty("mobyInstanceCount").GetInt32(), world.DynamicObjects!.Count);
            Assert.True(world.TotalDynamicTriangles +
                (world.AnimatedMeshes?.Sum(m => m.TriangleCount) ?? 0) >=
                row.GetProperty("expandedMobyTriangles").GetInt32(),
                $"UYA table {table} lost presented Moby geometry after recovery.");
        }
    }

    private static HashSet<(int ClassId, int InstanceId)> PresentedMobyActors(OBP.Runtime.RuntimeWorld world)
    {
        var presented = world.DynamicObjects!
            .Where(item => item.Meshes.Count > 0)
            .Select(item => (ClassId: item.NativeClassId, InstanceId: item.InstanceIndex))
            .ToHashSet();
        const string prefix = "uya-preview-moby";
        foreach (var mesh in world.AnimatedMeshes ?? [])
        {
            if (!mesh.Name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            int split = mesh.Name.IndexOf("_i", prefix.Length, StringComparison.Ordinal);
            int end = split < 0 ? -1 : mesh.Name.IndexOf("_s", split + 2, StringComparison.Ordinal);
            if (split > prefix.Length && end > split + 2 &&
                int.TryParse(mesh.Name.AsSpan(prefix.Length, split - prefix.Length), out int oClass) &&
                int.TryParse(mesh.Name.AsSpan(split + 2, end - split - 2), out int instance))
                presented.Add((oClass, instance));
        }
        return presented;
    }

    [SkippableFact]
    public void RecoveredVeldinPreviewPreservesAnimatedActorAndFrameRoster()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_UYA_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var world = Rac3WorldImport.BuildObserved(reader, 1, nativeMobyPoseOverride: true).World;
        var animated = Assert.IsAssignableFrom<IReadOnlyList<OBP.Runtime.RuntimeAnimatedMesh>>(world.AnimatedMeshes);
        Assert.Equal(45, animated.Count);
        Assert.Equal(37_922, animated.Sum(m => m.TriangleCount));
        Assert.All(animated, m => Assert.All(m.Frames.SelectMany(frame => frame),
            value => Assert.True(double.IsFinite(value))));

        // This class is the native-sphere outlier. Verify its rendered preview
        // uses native inverse-bind poses, not the old rotation-only evaluator.
        var opened = Rac3LevelCore.Open(reader, 1);
        var gameplay = Rac3Gameplay.Read(opened.GameplayReader);
        int texCount = Rac3Assets.ReadTextures(opened.Core, Rac3Assets.TextureTable.Moby).Count;
        var cls = Rac3Assets.ReadMobyClasses(opened.Core, texCount, nativePoseOverride: true).Decoded[6317];
        var sequence = cls.Sequences.Single(s => s.Index == 4);
        var native = Ps2MobyNativePose.Pose(cls.NativePoseSource!, cls.Mesh, sequence.Frames[0]);
        var instance = gameplay.MobyInstances.Single(m => m.Index == 477 && m.OClass == 6317);
        var transform = Rac3Gameplay.MobyTransform(instance);
        var preview = animated.First(m => m.Name.StartsWith("uya-preview-moby6317_i477_", StringComparison.Ordinal));
        for (int i = 0; i < Math.Min(30, native.Length); i += 3)
        {
            double x = native[i], y = native[i + 2], z = native[i + 1];
            var expected = new[]
            {
                transform[0] * x + transform[4] * y + transform[8] * z + transform[12],
                transform[1] * x + transform[5] * y + transform[9] * z + transform[13],
                transform[2] * x + transform[6] * y + transform[10] * z + transform[14],
            };
            for (int k = 0; k < 3; k++)
                Assert.InRange(Math.Abs(preview.Frames[0][i + k] - expected[k]), 0, 1e-5);
        }
    }
}
