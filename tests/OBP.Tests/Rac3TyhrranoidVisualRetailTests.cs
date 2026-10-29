using System.Security.Cryptography;
using OBP.IO;
using OBP.PS2.Geometry;
using OBP.RAC3.Gameplay;
using OBP.RAC3.Level;

namespace OBP.Tests;

/// <summary>
/// Retail-backed visual-class provenance for issue #297. Mesh silhouette
/// classification is a separately inspected geometry observation, not an AI
/// or native animation behavior assertion.
/// </summary>
public sealed class Rac3TyhrranoidVisualRetailTests
{
    private const string OneEyePositionSha256 =
        "276AD55B98180F7CBBC8020A19123F0B5A96930CB990BEB41F4CC188741EEF76";

    [SkippableFact]
    public void OneEyedClass5821UsesAnIdenticalRetailMeshAcrossEightAuthoredTables()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_UYA_ISO is unavailable.");
        using var reader = new FileRandomAccessReader(iso!);
        var population = new (int Table, int Instances)[]
        {
            (1, 62), (4, 126), (9, 75), (12, 30),
            (13, 66), (16, 32), (18, 30), (29, 50),
        };

        int total = 0;
        int[]? referenceTopology = null;
        foreach (var (table, count) in population)
        {
            var level = Rac3LevelCore.Open(reader, table);
            var gameplay = Rac3Gameplay.Read(level.GameplayReader);
            int found = gameplay.MobyInstances.Count(moby => moby.OClass == 5821);
            Assert.Equal(count, found);
            total += found;

            int textureCount = Rac3Assets.ReadTextures(
                level.Core, Rac3Assets.TextureTable.Moby).Count;
            var models = Rac3Assets.ReadMobyClasses(level.Core, textureCount, nativePoseOverride: true);
            Assert.True(models.Decoded.TryGetValue(5821, out var visual));
            Assert.NotNull(visual);
            Assert.Equal(1203, visual!.Mesh.Indices.Length / 3);
            Assert.Equal(67, visual.Joints.Count);
            Assert.Equal(13, visual.Sequences.Count);
            Assert.True(visual.Mesh.SkinningApplied);
            Assert.True(visual.Mesh.NativeInitialPoseApplied);
            Assert.Equal(OneEyePositionSha256, PositionHash(visual.Mesh.Positions));
            if (referenceTopology is null) referenceTopology = visual.Mesh.Indices.ToArray();
            else Assert.Equal(referenceTopology, visual.Mesh.Indices);
        }

        Assert.Equal(471, total);
    }

    [SkippableFact]
    public void Table1TwoEyedControlModelIsDifferentFromTheOneEyedClass()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_UYA_ISO is unavailable.");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac3LevelCore.Open(reader, 1);
        var gameplay = Rac3Gameplay.Read(level.GameplayReader);
        Assert.Equal(23, gameplay.MobyInstances.Count(moby => moby.OClass == 5860));

        int textureCount = Rac3Assets.ReadTextures(
            level.Core, Rac3Assets.TextureTable.Moby).Count;
        var models = Rac3Assets.ReadMobyClasses(level.Core, textureCount, nativePoseOverride: true);
        Assert.True(models.Decoded.TryGetValue(5860, out var twoEye));
        Assert.NotNull(twoEye);
        Assert.Equal(1300, twoEye!.Mesh.Indices.Length / 3);
        Assert.Equal(72, twoEye.Joints.Count);
        Assert.NotEqual(OneEyePositionSha256, PositionHash(twoEye.Mesh.Positions));
    }

    [SkippableFact]
    public void AllThirteenClass5821ClipsPoseWithinTheExistingNativeTenPercentSphereTolerance()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_UYA_ISO is unavailable.");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac3LevelCore.Open(reader, 1);
        int textureCount = Rac3Assets.ReadTextures(
            level.Core, Rac3Assets.TextureTable.Moby).Count;
        var cls = Rac3Assets.ReadMobyClasses(level.Core, textureCount, nativePoseOverride: true).Decoded[5821];

        Assert.NotNull(cls.NativePoseSource);
        Assert.Equal(13, cls.Sequences.Count);
        // The already recovered TABLE1 state-8 -> state-10 branch selects
        // action/sequence byte 12; the rest of the action controller is unknown.
        Assert.Equal(12, Rac3Class5821Actor.NativeState10SelectedAction);
        Assert.Equal(20, cls.Sequences.Single(s =>
            s.Index == Rac3Class5821Actor.NativeState10SelectedAction).Frames.Count);
        int frames = 0;
        foreach (var sequence in cls.Sequences)
        {
            Assert.NotNull(sequence.BoundingSphere);
            var sphere = sequence.BoundingSphere!.Value;
            double scale = cls.Mesh.Scale / 1024d;
            double radius = sphere.Radius * scale;
            Assert.True(radius > 0 && double.IsFinite(radius));
            double cx = sphere.X * scale, cy = sphere.Y * scale, cz = sphere.Z * scale;
            foreach (var frame in sequence.Frames)
            {
                Assert.True(frame.ExtraChannelsResolved);
                double[] posed = Ps2MobyNativePose.Pose(cls.NativePoseSource!, cls.Mesh, frame);
                Assert.Equal(cls.Mesh.Positions.Length, posed.Length);
                for (int i = 0; i < posed.Length; i += 3)
                {
                    double dx = posed[i] - cx, dy = posed[i + 1] - cy, dz = posed[i + 2] - cz;
                    double ratio = Math.Sqrt(dx * dx + dy * dy + dz * dz) / radius;
                    // Same bounded native-pose preview policy as Rac3WorldImport.Animation,
                    // not a claim that every frame fits the approximate retail sphere exactly.
                    Assert.InRange(ratio, 0d, 1.10d);
                }
                frames++;
            }
        }
        Assert.Equal(192, frames);
    }

    private static string PositionHash(double[] positions)
    {
        byte[] bytes = new byte[positions.Length * sizeof(double)];
        Buffer.BlockCopy(positions, 0, bytes, 0, bytes.Length);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
