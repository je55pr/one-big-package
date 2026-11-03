using OBP.IO;
using OBP.PS2.Collision;
using OBP.PS2.Geometry;
using OBP.PS2.Textures;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1LevelTests
{
    [SkippableFact]
    public void Level0_NativeCoreTerrainTexturesAndCollisionMatchReferenceEvidence()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);

        var catalogue = Rac1DiscIndex.Read(reader);
        Assert.Equal(1, catalogue.Version);
        Assert.Equal(0x2960, catalogue.DeclaredSize);
        Assert.Equal(19, catalogue.Levels.Count);

        var level = catalogue.Levels.Single(l => l.LevelId == 0);
        Assert.Equal(0, level.TableSlot);
        Assert.Equal(1_885_903u, level.HeaderLba);
        Assert.Equal(19_462u, level.TableRawSecondWord);
        Assert.Equal(new uint[] { 1_885_908, 1_892_470, 1_892_680, 1_892_890 }, level.CoreRanges.Select(r => r.OffsetSectors));
        Assert.Equal(new uint[] { 6_562, 210, 210, 7 }, level.CoreRanges.Select(r => r.SizeSectors));

        var core = Rac1LevelCore.Open(reader, level);
        Assert.Equal(32_160, core.Index.Length);
        Assert.Equal(16_766_912, core.Assets.Length);
        Assert.Equal(9_515_621, core.Header.AssetsCompressedSize);
        Assert.Equal(16_766_912, core.Header.AssetsDecompressedSize);
        Assert.Equal(0, core.Header.TfragsOffset);
        Assert.Equal(0x113f40, core.Header.TfragsEnd);
        Assert.Equal(0x1fe7c0, core.Header.CollisionOffset);
        Assert.Equal(0x2c1700, core.Header.CollisionEnd);
        Assert.Equal(78, core.Header.TfragTextures.Count);

        Assert.Equal(1_556_416, core.Header.SkyOffset);
        Assert.Equal(2_090_944, core.Header.CollisionOffset);
        var skyBytes = core.Assets.AsSpan(
            core.Header.SkyOffset,
            core.Header.CollisionOffset - core.Header.SkyOffset).ToArray();
        var sky = RcSky.Read(skyBytes);
        Assert.Equal(5, sky.Shells.Count);
        Assert.Equal(8, sky.Textures.Count);
        Assert.Equal(2_211, sky.Shells.Sum(s => s.Positions.Length / 3));
        Assert.Equal(2_366, sky.Shells.Sum(s => s.Indices.Length / 3));
        Assert.All(sky.Textures, t => Assert.Equal(t.Width * t.Height * 4, t.Rgba.Length));
        Assert.All(sky.Shells, shell =>
        {
            Assert.Equal(shell.Positions.Length / 3 * 2, shell.Uvs.Length);
            Assert.Equal(shell.Positions.Length / 3, shell.Alpha.Length);
            Assert.Equal(shell.Indices.Length / 3, shell.TriangleTextureIds.Length);
            Assert.All(shell.Indices, i => Assert.InRange(i, 0, shell.Positions.Length / 3 - 1));
        });

        var tfragBytes = core.Assets.AsSpan(core.Header.TfragsOffset, core.Header.TfragsEnd - core.Header.TfragsOffset).ToArray();
        var tf = RcTfrag.Read(tfragBytes);
        Assert.Equal(460, tf.TfragCount);
        Assert.Equal(24_758, tf.Positions.Length / 3);
        Assert.Equal(24_520, tf.Indices.Length / 3);
        Assert.Equal(Enumerable.Range(0, 78), tf.TextureIds);
        Assert.Equal((86.6689453125, 5.9990234375, 67.9599609375), tf.BoundsMin);
        Assert.Equal((233.083984375, 43.5087890625, 307.4794921875), tf.BoundsMax);

        var textures = RcLevelTextureTable.Read(
            core.Index, core.Assets, core.GsRam, core.Header.TexturesBaseOffset,
            new RcLevelTextureTable.Range(core.Header.TfragTextures.Count, core.Header.TfragTextures.Offset));
        Assert.Equal(78, textures.Count);
        Assert.Equal(46, textures.Count(t => t.Width == 128 && t.Height == 128));
        Assert.Equal(30, textures.Count(t => t.Width == 64 && t.Height == 64));
        Assert.Equal(2, textures.Count(t => t.Width == 32 && t.Height == 32));
        Assert.All(textures, t => Assert.Equal(t.Width * t.Height * 4, t.Rgba.Length));

        var collBytes = core.Assets.AsSpan(core.Header.CollisionOffset, core.Header.CollisionEnd - core.Header.CollisionOffset).ToArray();
        var coll = RcCollision.Read(collBytes);
        Assert.Equal(5_783, coll.Octants.Count);
        Assert.Equal(109_751, coll.Positions.Length / 3);
        Assert.Equal(86_184, coll.Triangles.Count);
        Assert.Equal(new[] { 9, 10, 12, 31 }, coll.MaterialIds);
        Assert.Equal(new OBP.Core.Math.Vec3(67.9375, 70.125, 10.1875), coll.Bounds.Min);
        Assert.Equal(new OBP.Core.Math.Vec3(207.9375, 311.1875, 82.0625), coll.Bounds.Max);
    }
}
