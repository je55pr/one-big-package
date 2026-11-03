using System.Buffers.Binary;
using OBP.Core.Math;
using OBP.IO;
using OBP.PS2.Collision;
using OBP.PS2.Compression;
using OBP.RAC2.Geometry;
using OBP.RAC2.Level;
using static OBP.Tests.Rac2RetailLevelFixtures;

namespace OBP.Tests;

/// <summary>
/// Checkpoint D equivalence: the native GC level pipeline must reproduce the
/// deterministic Oozla (LEVEL1) numbers recorded in
/// <c>docs/TS_REFERENCE_BASELINE.md</c>. Grows one assertion block per ported
/// decoder. Retail-gated on <c>OBP_GC_ISO</c>.
/// 
/// This class covers the level WAD container and the sections decoded straight from its lumps. Gameplay lumps,
/// decoded asset digests, boot-overlay witnesses and the assembled world import live in
/// <c>Rac2LevelGameplayTests</c>, <c>Rac2LevelDecodedAssetTests</c>, <c>Rac2RetailOverlayWitnessTests</c> and
/// <c>Rac2LevelWorldImportTests</c>.
/// </summary>
public class Rac2LevelTests
{
    [SkippableFact]
    public void Level1_OuterWadHeader()
    {
        var iso = RequireIso();

        var (_, header) = OpenLevelWad(iso, 1);
        Assert.Equal(0x60, header.HeaderSizeField);
        Assert.Equal(1, header.LevelId);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 7, 8 }, header.Lumps.Where(l => l.Present).Select(l => l.Slot).ToArray());

        // LEVEL21.WAD self-reports levelId 30 (file index ≠ engine id).
        var (_, h21) = OpenLevelWad(iso, 21);
        Assert.Equal(30, h21.LevelId);
    }

    [SkippableFact]
    public void Level1_DecompressedCoreMatchesTheBaseline()
    {
        var iso = RequireIso();

        var (wad, header) = OpenLevelWad(iso, 1);
        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0));

        Assert.Equal(19_023_392, core.Assets.Length);
        Assert.Equal(core.Header.AssetsDecompressedSize, core.Assets.Length);
        Assert.Equal(34_016, core.Index.Length);
        Assert.Equal(569_344, core.GsRam.Length);
        Assert.Equal(402, core.SectionBoundaries.Count);

        Assert.Equal(0, core.Header.Tfrags);
        Assert.Equal(1_247_488, core.Header.Occlusion);
        Assert.Equal(1_800_448, core.Header.Sky);
        Assert.Equal(2_044_480, core.Header.Collision);
        Assert.Equal(4_881_408, core.Header.TexturesBaseOffset);

        Assert.Equal((227, 192), (core.Header.MobyClasses.Count, core.Header.MobyClasses.Offset));
        Assert.Equal((91, 7456), (core.Header.TieClasses.Count, core.Header.TieClasses.Offset));
        Assert.Equal((24, 10368), (core.Header.ShrubClasses.Count, core.Header.ShrubClasses.Offset));
        Assert.Equal(77, core.Header.TfragTextures.Count);
        Assert.Equal(79, core.Header.TieTextures.Count);
        Assert.Equal(38, core.Header.ShrubTextures.Count);
    }

    [SkippableFact]
    public void Level1_LevelSettingsMatchTheBaseline()
    {
        var iso = RequireIso();

        var (wad, header) = OpenLevelWad(iso, 1);
        var s = Rac2LevelSettings.Read(Rac2LevelWad.RequireLump(wad, header, 2));

        Assert.Equal(0f, s.DeathHeight);
        Assert.False(s.IsSphericalWorld);
        Assert.Equal(278.04974365234375f, s.ShipPosition.X);
        Assert.Equal(411.412841796875f, s.ShipPosition.Y);
        Assert.Equal(115.29898834228516f, s.ShipPosition.Z);
        Assert.Equal(1.7839138507843018f, s.ShipRotationZ);
        Assert.Equal((6 / 255.0, 16 / 255.0, 12 / 255.0), s.BackgroundColour);
        Assert.Equal((10 / 255.0, 40 / 255.0, 30 / 255.0), s.FogColour);
        Assert.Equal(25600f, s.FogNearDistance);
        Assert.Equal(230400f, s.FogFarDistance);
        Assert.Equal(255f, s.FogNearIntensity);
        Assert.Equal(178.5f, s.FogFarIntensity);
    }

    [SkippableFact]
    public void Level1_ChunkCollisionMatchesTheBaseline()
    {
        var iso = RequireIso();

        var (wad, header) = OpenLevelWad(iso, 1);

        var meshes = new List<RcCollision.Mesh>();
        foreach (int slot in new[] { 4, 5, 6 })
        {
            var chunk = Rac2LevelWad.OpenLump(wad, header, slot);
            if (chunk is null)
            {
                continue;
            }

            var head = chunk.Read(0, 8);
            int collOffset = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(4));
            if (collOffset <= 0)
            {
                continue;
            }

            meshes.Add(RcCollision.Read(WadLz.ReadBlock(chunk, collOffset).Data));
        }

        Assert.Equal(2, meshes.Count);

        Assert.Equal(22_404, meshes[0].Octants.Count);
        Assert.Equal(368_937, meshes[0].Positions.Length / 3);
        Assert.Equal(308_108, meshes[0].Triangles.Count);
        Assert.Equal(346, meshes[0].HeroGroupCount);
        Assert.Equal(new[] { 2, 3, 4, 9, 10, 12, 31, 63, 73, 95, 127 }, meshes[0].MaterialIds);
        Assert.Equal(new Vec3(158.75, 245.25, 97.65625), meshes[0].Bounds.Min);
        Assert.Equal(new Vec3(531.5625, 650.9375, 157.046875), meshes[0].Bounds.Max);

        Assert.Equal(4_210, meshes[1].Octants.Count);
        Assert.Equal(33_840, meshes[1].Positions.Length / 3);
        Assert.Equal(20_815, meshes[1].Triangles.Count);
        Assert.Equal(25, meshes[1].HeroGroupCount);

        Assert.Equal(328_923, meshes.Sum(m => m.Triangles.Count)); // baseline total
    }

    [SkippableFact]
    public void Level1_Chunk0TfragsMatchTheBaseline()
    {
        var iso = RequireIso();

        var (wad, header) = OpenLevelWad(iso, 1);
        var chunk = Rac2LevelWad.RequireLump(wad, header, 4);
        int tfragOffset = BinaryPrimitives.ReadInt32LittleEndian(chunk.Read(0, 8).AsSpan());
        var tf = Rac2Tfrag.Read(WadLz.ReadBlock(chunk, tfragOffset).Data);

        Assert.Equal(401, tf.TfragCount);
        Assert.Equal(28_418, tf.Positions.Length / 3);
        Assert.Equal(26_192, tf.Indices.Length / 3);
        Assert.Equal(Enumerable.Range(0, 77), tf.TextureIds);
        Assert.Equal((226.9443359375, 100.9150390625, 304.6904296875), tf.BoundsMin);
        Assert.Equal((469.0751953125, 163.1513671875, 650.443359375), tf.BoundsMax);
    }
}
