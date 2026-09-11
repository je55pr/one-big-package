using System.Buffers.Binary;
using OBP.Core.Math;
using OBP.IO;
using OBP.PS2;
using OBP.PS2.Collision;
using OBP.PS2.Elf;
using OBP.PS2.Compression;
using OBP.PS2.Iso;
using OBP.PS2.Geometry;
using OBP.RAC2;
using OBP.RAC2.Geometry;
using OBP.RAC2.Gameplay;
using OBP.RAC2.Level;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

/// <summary>
/// Checkpoint D equivalence: the native GC level pipeline must reproduce the
/// deterministic Oozla (LEVEL1) numbers recorded in
/// <c>docs/TS_REFERENCE_BASELINE.md</c>. Grows one assertion block per ported
/// decoder. Retail-gated on <c>OBP_GC_ISO</c>.
/// </summary>
public class GcLevelTests
{
    private static (IRandomAccessReader wad, GcLevelWad.Header header) OpenLevelWad(string iso, int level)
    {
        var reader = new FileRandomAccessReader(iso);
        var fs = Iso9660Filesystem.Open(reader);
        var wad = fs.OpenFile($"/G/LEVEL{level}.WAD") ?? throw new FileNotFoundException($"/G/LEVEL{level}.WAD");
        return (wad, GcLevelWad.ReadHeader(wad));
    }

    [SkippableFact]
    public void Level1_OuterWadHeader()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        var (_, header) = OpenLevelWad(iso!, 1);
        Assert.Equal(0x60, header.HeaderSizeField);
        Assert.Equal(1, header.LevelId);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 7, 8 }, header.Lumps.Where(l => l.Present).Select(l => l.Slot).ToArray());

        // LEVEL21.WAD self-reports levelId 30 (file index ≠ engine id).
        var (_, h21) = OpenLevelWad(iso!, 21);
        Assert.Equal(30, h21.LevelId);
    }

    [SkippableFact]
    public void Level1_DecompressedCoreMatchesTheBaseline()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        var (wad, header) = OpenLevelWad(iso!, 1);
        var core = GcLevelCore.Open(GcLevelWad.RequireLump(wad, header, 0));

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
    public void AllLevels_BoltRewardPercentageTablesMatchRetailOverlay()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        byte[] expected = [
            .. GcBoltReward.AuthoredBank0, .. GcBoltReward.AuthoredBank1,
            .. GcBoltReward.EmissionBank0, .. GcBoltReward.EmissionBank1
        ];

        for (int level = 0; level < 27; level++)
        {
            var (wad, header) = OpenLevelWad(iso!, level);
            var overlay = GcLevelOverlay.Open(GcLevelWad.RequireLump(wad, header, 0));
            Assert.Equal(expected, overlay.ReadVirtual(GcBoltReward.AuthoredBank0Address, expected.Length));
        }
    }

    [SkippableFact]
    public void BootRewardSelectorBaseBlockStartsZeroInRetailFileImage()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        using var isoReader = new FileRandomAccessReader(iso!);
        var disc = Ps2Boot.OpenDisc(isoReader);
        var programHeaders = Ps2Boot.ReadBootProgramHeaders(disc);
        var selectors = Elf32Reader.ReadVirtualRange(
            disc.BootExecutable, programHeaders,
            GcBoltReward.PackedSelectorBaseAddress, GcBoltReward.PackedSelectorTableBytes);

        Assert.Equal(GcBoltReward.PackedSelectorTableBytes, selectors.Length);
        Assert.All(selectors, value => Assert.Equal(0, value));
    }

    [SkippableFact]
    public void Level1_State21WeaponObjectIdentityMatchesRetail()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        var (wad, header) = OpenLevelWad(iso!, 1);
        var overlay = GcLevelOverlay.Open(GcLevelWad.RequireLump(wad, header, 0));

        // This transition only falls through to SetPlayerState(21, 1) when the
        // effective live weapon index at player-global +0x1248 equals 10.
        var transition = overlay.ReadVirtual(0x002C52D4, 0x1C);
        uint[] expectedTransition =
        [
            0x8C631248, // lw v1,+0x1248(v1)
            0x2402000A, // li v0,10
            0x54620006, // bnel v1,v0,...
            0x8482034E, // branch-likely delay slot
            0x24040015, // li a0,21
            0x0C0AFAE4, // jal 0x002BEB90
            0x24050001, // li a1,1
        ];
        Assert.Equal(expectedTransition, Words(transition));

        // The live weapon allocator stores its effective index beside the live
        // Moby pointer, and index 10 has an explicit special path.
        Assert.Equal(0xAE121248u, Word(overlay.ReadVirtual(0x002B072C, 4)));
        Assert.Equal(0xAE051220u, Word(overlay.ReadVirtual(0x002B0788, 4)));
        Assert.Equal(0x1642000Du, Word(overlay.ReadVirtual(0x002B07C4, 4)));

        // Retail's index map is identity at slot 10. Metadata record 10 has
        // live Moby class 71 at +0x14, the class passed to the normal allocator.
        using var isoReader = new FileRandomAccessReader(iso!);
        var disc = Ps2Boot.OpenDisc(isoReader);
        var programHeaders = Ps2Boot.ReadBootProgramHeaders(disc);
        var mapEntry = Elf32Reader.ReadVirtualRange(disc.BootExecutable, programHeaders, 0x00139572, 1);
        Assert.Equal((byte)GcPlayerWeaponIdentity.State21EffectiveWeaponIndex, mapEntry[0]);
        Assert.Equal((uint)GcPlayerWeaponIdentity.State21MobyClass, Word(overlay.ReadVirtual(0x00264074, 4)));

        // State 21 checks that it is still active before calling the launch
        // helper. That helper loads +0x1220 and drives the live Moby to state 10.
        Assert.Equal(0x8E032294u, Word(overlay.ReadVirtual(0x002BB0A4, 4)));
        Assert.Equal(0x0C0AE7BCu, Word(overlay.ReadVirtual(0x002BB0B0, 4)));
        Assert.Equal(0x8E141220u, Word(overlay.ReadVirtual(0x002B9FAC, 4)));
        Assert.Equal(0x2402000Au, Word(overlay.ReadVirtual(0x002B9FD4, 4)));
        Assert.Equal(0xA2820020u, Word(overlay.ReadVirtual(0x002B9FE4, 4)));
    }

    [SkippableFact]
    public void Level1_LevelSettingsMatchTheBaseline()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        var (wad, header) = OpenLevelWad(iso!, 1);
        var s = GcLevelSettings.Read(GcLevelWad.RequireLump(wad, header, 2));

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
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        var (wad, header) = OpenLevelWad(iso!, 1);

        var meshes = new List<RcCollision.Mesh>();
        foreach (int slot in new[] { 4, 5, 6 })
        {
            var chunk = GcLevelWad.OpenLump(wad, header, slot);
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
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        var (wad, header) = OpenLevelWad(iso!, 1);
        var chunk = GcLevelWad.RequireLump(wad, header, 4);
        int tfragOffset = BinaryPrimitives.ReadInt32LittleEndian(chunk.Read(0, 8).AsSpan());
        var tf = GcTfrag.Read(WadLz.ReadBlock(chunk, tfragOffset).Data);

        Assert.Equal(401, tf.TfragCount);
        Assert.Equal(28_418, tf.Positions.Length / 3);
        Assert.Equal(26_192, tf.Indices.Length / 3);
        Assert.Equal(Enumerable.Range(0, 77), tf.TextureIds);
        Assert.Equal((226.9443359375, 100.9150390625, 304.6904296875), tf.BoundsMin);
        Assert.Equal((469.0751953125, 163.1513671875, 650.443359375), tf.BoundsMax);
    }

    [SkippableFact]
    public void Level1_GameplayInstanceCountsMatchTheBaseline()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        var (wad, header) = OpenLevelWad(iso!, 1);
        var gameplay = GcInstances.Read(GcLevelWad.RequireLump(wad, header, 2));

        Assert.Equal(1617, gameplay.TieInstances.Count);
        Assert.Equal(2825, gameplay.ShrubInstances.Count);
        Assert.Equal(748, gameplay.MobyInstances.Count);

        // Gameplay identity/state needed by the dynamic-Moby bridge. Oozla has
        // 190 authored class-500 Bolt Crates; each retains its native 0x88
        // instance record and independently resolved 0x110-byte PVar.
        var boltCrates = gameplay.MobyInstances.Where(m => m.OClass == 500).ToArray();
        Assert.Equal(190, boltCrates.Length);
        Assert.All(boltCrates, m =>
        {
            Assert.Equal(0x88, m.RawInstance.Length);
            Assert.Equal(0x20, m.ModeBits);
            Assert.True(m.PVarIndex >= 0);
            Assert.NotNull(m.PVarData);
            Assert.Equal(0x110, m.PVarData!.Length);
        });
        Assert.Equal(boltCrates.Length, boltCrates.Select(m => m.PVarIndex).Distinct().Count());
        Assert.Equal(189, boltCrates.Count(m => m.Bolts == 13));
        Assert.Single(boltCrates, m => m.Bolts == 14);
        Assert.All(boltCrates, m => Assert.InRange(m.Bolts, 13, 14));

        // The deterministic Oozla showcase deliberately targets the first authored
        // class-500 instance. Pin its retail identity and the neutral fresh-session
        // payout contract so the visible crate -> pickups loop cannot drift.
        var showcaseCrate = boltCrates.OrderBy(m => m.Index).First();
        Assert.Equal(31, showcaseCrate.Index);
        Assert.Equal(73, showcaseCrate.Uid);
        Assert.Equal(13, showcaseCrate.Bolts);
        Assert.Equal(0, showcaseCrate.PVarData![0xC8]);
        var showcasePayout = new GcFreshBoltSession().PlanClass500Payout(
            showcaseCrate.Uid, showcaseCrate.Bolts, rewardMultiplierByte: 0,
            progressionLikeInput: 0, rngMod2: 1);
        Assert.Equal(13, showcasePayout.RewardCentreValue);
        Assert.Equal(new[] { 5, 5, 1 }, showcasePayout.PhysicalPickups.Select(p => p.Denomination));
        Assert.Equal(2, showcasePayout.DeferredValue);

        // Directional lights (gameplay ptr 0x04) — the main GC light type.
        Assert.Equal(3, gameplay.DirLights.Count);
        var dl0 = gameplay.DirLights[0];
        Assert.Equal(0.37254903f, dl0.ColourA.X, 5);
        Assert.Equal(0.46666667f, dl0.ColourA.Y, 5);
        Assert.Equal(0.83867055f, dl0.DirectionA.Y, 5);
        Assert.Equal(-0.54463905f, dl0.DirectionA.Z, 5);

        // Per-moby baked ambient (0x74) + directional-light index (0x80).
        Assert.Equal(new[] { 28 / 255f, 45 / 255f, 51 / 255f }, new[] { gameplay.MobyInstances[0].LightColour.R, gameplay.MobyInstances[0].LightColour.G, gameplay.MobyInstances[0].LightColour.B });
        var idxHistogram = gameplay.MobyInstances.GroupBy(mi => mi.LightIndex).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(506, idxHistogram[0]);
        Assert.Equal(168, idxHistogram[1]);
        Assert.Equal(61, idxHistogram[2]);
        Assert.Equal(13, idxHistogram[3855]); // out-of-range sentinel -> ambient only

        // Point lights (gameplay ptr 0x80) — only affect mobies. Oozla has 2.
        Assert.Equal(2, gameplay.PointLights.Count);
        Assert.Equal(353.703125f, gameplay.PointLights[0].Position.X, 3);
        Assert.InRange(gameplay.PointLights[0].Radius, 15f, 45f);
        Assert.All(gameplay.PointLights, p => Assert.True(p.Radius > 0));

        // Env sample points (gameplay ptr 0x8c) — the per-region atmosphere probes.
        Assert.Equal(10, gameplay.EnvSamples.Count);
        Assert.Equal(0, gameplay.EnvSamples[0].HeroLightIndex);
        Assert.Equal(new[] { 28 / 255f, 45 / 255f, 51 / 255f }, new[] { gameplay.EnvSamples[0].HeroColour.R, gameplay.EnvSamples[0].HeroColour.G, gameplay.EnvSamples[0].HeroColour.B });
        Assert.Null(gameplay.EnvSamples[0].Fog); // Oozla samples carry no fog override
        Assert.All(gameplay.EnvSamples, es => Assert.Null(es.Fog));

        // Env transitions (gameplay ptr 0x84) — doorway lighting/fog blend volumes.
        Assert.Empty(gameplay.EnvTransitions); // Oozla has none

        // Endako has 12 (hero-only); Grelbin has 2 with a fog blend.
        var (endakoWad, endakoHeader) = OpenLevelWad(iso!, 3);
        var endako = GcInstances.Read(GcLevelWad.RequireLump(endakoWad, endakoHeader, 2));
        Assert.Equal(12, endako.EnvTransitions.Count);
        Assert.True(endako.EnvTransitions[0].EnableHero);
        Assert.False(endako.EnvTransitions[0].EnableFog);
        Assert.Equal(16, endako.EnvTransitions[0].InverseMatrix.Length);

        var (grelbinWad, grelbinHeader) = OpenLevelWad(iso!, 19);
        var grelbin = GcInstances.Read(GcLevelWad.RequireLump(grelbinWad, grelbinHeader, 2));
        Assert.Equal(2, grelbin.EnvTransitions.Count);
        Assert.True(grelbin.EnvTransitions[0].EnableFog);
        Assert.Equal(150f, grelbin.EnvTransitions[0].StateA.FogFarDistance, 1);
        Assert.Equal(235f, grelbin.EnvTransitions[0].StateB.FogFarDistance, 1);
    }

    [SkippableTheory]
    [InlineData(GcLevelTextures.Table.Tfrag, 77, "ce547004b8eeb754e9baae636ecc58da5d22ba100eaa85e25caf432b48622913")]
    [InlineData(GcLevelTextures.Table.Tie, 79, "9542d4d0bb1c0b7749297b7c9db8453131dac257204f54f7e77a9b828d5ac5b2")]
    [InlineData(GcLevelTextures.Table.Shrub, 38, "d95521faf7fed21b18159f7986e2784d0148e6949a794b31001048b36fde4cf6")]
    [InlineData(GcLevelTextures.Table.Moby, 199, "ed5fca629a9ff7d0448241ea18e9b4a24be426d6b50d01d3e62649757c9d2aa2")]
    public void Level1_DecodedTexturesMatchTypeScriptPixels(GcLevelTextures.Table table, int count, string expectedSha256)
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        var (wad, header) = OpenLevelWad(iso!, 1);
        var core = GcLevelCore.Open(GcLevelWad.RequireLump(wad, header, 0));
        var textures = GcLevelTextures.Read(core, table);

        Assert.Equal(count, textures.Count);

        using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var t in textures)
        {
            sha.AppendData([(byte)t.Index, (byte)(t.Width & 255), (byte)(t.Height & 255)]);
            sha.AppendData(t.Rgba);
        }

        Assert.Equal(expectedSha256, Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant());
    }

    [SkippableFact]
    public void Level1_TieClassesMatchTypeScript()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        var (wad, header) = OpenLevelWad(iso!, 1);
        var core = GcLevelCore.Open(GcLevelWad.RequireLump(wad, header, 0));
        var classes = GcTie.ReadClasses(core);

        Assert.Equal(91, classes.Count);
        var sha = HashClassMeshes(classes.OrderBy(kv => kv.Key)
            .Select(kv => (kv.Key, kv.Value.Mesh.Indices, kv.Value.Mesh.Positions, kv.Value.Mesh.TriangleMaterialSlots, kv.Value.TriangleTextureIds)));
        Assert.Equal("826999b974d58093089e823d84bf2025618aafedd2d8a6877d476abdd66b9247", sha);
    }

    [SkippableFact]
    public void Level1_ShrubClassesMatchTypeScript()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        var (wad, header) = OpenLevelWad(iso!, 1);
        var core = GcLevelCore.Open(GcLevelWad.RequireLump(wad, header, 0));
        var classes = GcShrub.ReadClasses(core);

        Assert.Equal(24, classes.Count);
        var sha = HashClassMeshes(classes.OrderBy(kv => kv.Key)
            .Select(kv => (kv.Key, kv.Value.Mesh.Indices, kv.Value.Mesh.Positions, kv.Value.Mesh.TriangleMaterialSlots, kv.Value.TriangleTextureIds)));
        Assert.Equal("fffd756fef1e6cf04c6578d1daa2dc84886e24a589f2acb7278bb963707b92ea", sha);
    }

    [SkippableFact]
    public void Level1_SkyMatchesTypeScript()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        var (wad, header) = OpenLevelWad(iso!, 1);
        var core = GcLevelCore.Open(GcLevelWad.RequireLump(wad, header, 0));
        var sky = GcSky.ReadLevelSky(core) ?? throw new InvalidDataException("Oozla has a sky section.");

        Assert.Equal(4, sky.Shells.Count);
        Assert.Equal(3, sky.Textures.Count);
        Assert.Equal(1694, sky.Shells.Sum(s => s.Indices.Length / 3));
        Assert.Equal(new[] { false, true, true, true }, sky.Shells.Select(s => s.Textured).ToArray());
        Assert.Equal(new[] { (128, 128), (512, 128), (512, 256) }, sky.Textures.Select(t => (t.Width, t.Height)).ToArray());

        using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        var scratch = new byte[4];
        void I32(int v) { BinaryPrimitives.WriteInt32LittleEndian(scratch, v); sha.AppendData(scratch); }

        foreach (var s in sky.Shells)
        {
            I32(s.Textured ? 1 : 0);
            I32(s.Indices.Length / 3);
            I32(s.Positions.Length / 3);
            foreach (var p in s.Positions) I32((int)System.Math.Floor(p * 100000 + 0.5));
            foreach (var i in s.Indices) I32(i);
            foreach (var t in s.TriangleTextureIds) I32(t);
            foreach (var a in s.Alpha) I32((int)System.Math.Floor(a * 10000 + 0.5));
        }

        foreach (var t in sky.Textures)
        {
            I32(t.Index);
            I32(t.Width);
            I32(t.Height);
            sha.AppendData(t.Rgba);
        }

        Assert.Equal("460655acbf524db0c3c211b1bf2d260e839cdc4f474c4161024ebc69781edd8d",
            Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant());
    }

    [SkippableFact]
    public void Level1_MobyClassesMatchTypeScript()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        var (wad, header) = OpenLevelWad(iso!, 1);
        var core = GcLevelCore.Open(GcLevelWad.RequireLump(wad, header, 0));
        var classes = GcMobyClasses.Read(core);

        Assert.Equal(180, classes.Count);
        Assert.Equal(172, classes.Count(kv => kv.Value.Mesh.Indices.Length > 0));
        Assert.Equal(34, classes.Count(kv => kv.Value.Mesh.SkinningApplied));
        Assert.Equal(84_968, classes.Sum(kv => kv.Value.Mesh.Indices.Length / 3));

        using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        var scratch = new byte[4];
        void I32(int v) { BinaryPrimitives.WriteInt32LittleEndian(scratch, v); sha.AppendData(scratch); }

        foreach (var (key, cl) in classes.OrderBy(kv => kv.Key))
        {
            var m = cl.Mesh;
            I32(key);
            I32(m.Indices.Length / 3);
            I32(m.Positions.Length / 3);
            I32(m.Skinned ? 1 : 0);
            I32(m.SkinningApplied ? 1 : 0);
            foreach (var p in m.Positions) I32((int)System.Math.Floor(p * 10000 + 0.5));
            foreach (var i in m.Indices) I32(i);
            foreach (var s in m.TriangleMaterialSlots) I32(s);
            foreach (var t in cl.TriangleTextureIds) I32(t);
        }

        Assert.Equal("96d43906e2b13740be4798db55dd24c3fd3e2946a81a3b6c4c2be002bdb60fbb",
            Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant());

        // Moby normals (spherical decode, bytes 0x08/0x09) — one per position,
        // unit length. Their exact bits are libm-dependent (sin/cos differ by a
        // ULP between C# and JS), so they are checked structurally, not hashed;
        // the shading they drive is approximate anyway.
        foreach (var (_, cl) in classes)
        {
            var m = cl.Mesh;
            Assert.Equal(m.Positions.Length, m.Normals.Length);
            for (int i = 0; i + 3 <= m.Normals.Length; i += 3)
            {
                double len = System.Math.Sqrt(
                    m.Normals[i] * m.Normals[i] + m.Normals[i + 1] * m.Normals[i + 1] + m.Normals[i + 2] * m.Normals[i + 2]);
                Assert.InRange(len, 0.999, 1.001);
            }
        }
    }

    [SkippableFact]
    public void Level1_MobySequencesMatchTypeScript()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        var (wad, header) = OpenLevelWad(iso!, 1);
        var core = GcLevelCore.Open(GcLevelWad.RequireLump(wad, header, 0));
        var classes = GcMobyClasses.Read(core);

        int jointTotal = classes.Sum(kv => kv.Value.Joints.Count);
        int seqFrames = classes.Sum(kv => kv.Value.Sequences.Sum(s => s.Frames.Count));
        Assert.Equal((180, 1929, 6476), (classes.Count, jointTotal, seqFrames));

        // Same digest scheme as reference-ts/_seqhash.mjs (see GC_MOBY.md): joint
        // parent + bind translation (×100), then per frame speed (×1000) and each
        // quaternion channel (×32768) as rounded s32.
        using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        var scratch = new byte[4];
        void I32(double v)
        {
            // JS Math.round: half rounds toward +∞ (not away from zero / banker's).
            BinaryPrimitives.WriteInt32LittleEndian(scratch, (int)System.Math.Floor(v + 0.5));
            sha.AppendData(scratch);
        }

        foreach (var (key, cl) in classes.OrderBy(kv => kv.Key))
        {
            I32(key);
            I32(cl.Joints.Count);
            I32(cl.Sequences.Count);
            foreach (var j in cl.Joints)
            {
                I32(j.Parent);
                I32((double)j.Bx * 100);
                I32((double)j.By * 100);
                I32((double)j.Bz * 100);
            }

            foreach (var s in cl.Sequences)
            {
                I32(s.Index);
                I32(s.Frames.Count);
                foreach (var f in s.Frames)
                {
                    I32((double)f.Speed * 1000);
                    foreach (var q in f.JointRotations)
                    {
                        I32((double)q.X * 32768);
                        I32((double)q.Y * 32768);
                        I32((double)q.Z * 32768);
                        I32((double)q.W * 32768);
                    }
                }
            }
        }

        Assert.Equal("1d5f8c3a5c624337c91c6acadfc7b56bcabbe61558aef0dd804d7181d017b26f",
            Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant());
    }

    [SkippableFact]
    public void Level1_WorldImportMatchesTheBaseline()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        using var reader = new FileRandomAccessReader(iso!);
        var world = GcWorldImport.Build(reader, 1);

        // Render geometry. tfrags now come from every chunk slot (spatial tiles),
        // not just chunk 0 — chunk 0 alone is only the region around the ship.
        // 2 oc1134 instances are lifted out as animated mobies (below).
        Assert.Equal(302, world.Meshes.Count);
        Assert.Equal(1_860_879, world.TotalRenderTriangles);
        Assert.Equal(21_470, world.TotalDynamicTriangles);
        Assert.Equal(1_882_349, world.TotalRenderTriangles + world.TotalDynamicTriangles);
        Assert.Equal(398, world.MaterialCount);

        int Meshes(string kind) => world.Meshes.Count(m => m.AssetKind == kind);
        int Tris(string kind) => world.Meshes.Where(m => m.AssetKind == kind).Sum(m => m.TriangleCount);

        Assert.Equal((80, 27_384), (Meshes("tfrag"), Tris("tfrag"))); // chunk 0: 77 / 26,192 + chunk 1: 3 / 1,192
        Assert.Equal((79, 674_252), (Meshes("tie"), Tris("tie")));
        Assert.Equal((38, 852_785), (Meshes("shrub"), Tris("shrub")));
        Assert.Equal((99, 304_186), (Meshes("moby"), Tris("moby")));
        Assert.Equal((1, 576), (Meshes("moby-marker"), Tris("moby-marker")));
        Assert.Equal((4, 1_694), (Meshes("sky"), Tris("sky")));
        Assert.Equal((1, 2), (Meshes("death-plane"), Tris("death-plane")));

        // First production dynamic-Moby slice: all class-500 Bolt Crates keep
        // per-instance identity/render/state instead of contributing to the
        // welded moby mesh. Static+dynamic triangle accounting is unchanged.
        Assert.NotNull(world.DynamicObjects);
        var dynamicCrates = world.DynamicObjects!.Where(o => o.NativeClassId == 500).ToArray();
        Assert.Equal(190, dynamicCrates.Length);
        Assert.All(dynamicCrates, o =>
        {
            Assert.Equal("rac2", o.SourceGame);
            Assert.Equal($"moby:{o.NativeClassId}", o.ModelRef);
            Assert.StartsWith("level:1:moby:", o.InteractionId);
            Assert.Equal(16, o.Transform.Matrix.Length);
            Assert.NotEmpty(o.Meshes);
            Assert.Contains(o.NativePayloads!, p => p.Format == "rac2-moby-instance-0x88" && p.Data.Length == 0x88);
            Assert.Contains(o.NativePayloads!, p => p.Format == "rac2-pvar" && p.Data.Length == 0x110);
        });

        var showcaseRuntimeCrate = Assert.Single(dynamicCrates, o => o.InstanceIndex == 31);
        var showcaseLifecycle = GcClass500Lifecycle.ApplyRecoveredBreak(
            showcaseRuntimeCrate, RuntimeEntityState.FromAuthored(showcaseRuntimeCrate));
        Assert.Equal(73, showcaseLifecycle.Authored.Uid);
        Assert.Equal(13, showcaseLifecycle.Authored.AuthoredBolts);
        Assert.Equal((byte?)0, showcaseLifecycle.Authored.PvarC8);
        Assert.Equal(GcClass500PostBreakRoute.Deactivate, showcaseLifecycle.Route);
        Assert.Equal(RuntimeEntityPresence.Inactive, showcaseLifecycle.EntityState.Presentation.Presence);

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

        // Every tfrag mesh carries the baked per-vertex RGBA (the level's static
        // lighting / AO) — one float4 per vertex, alpha 1, channels in 0..1.
        foreach (var m in world.Meshes.Where(m => m.AssetKind == "tfrag"))
        {
            Assert.NotNull(m.Colors);
            Assert.Equal(m.Positions.Length / 3 * 4, m.Colors!.Length);
            for (int i = 0; i < m.Colors.Length; i += 4)
            {
                Assert.InRange(m.Colors[i], 0f, 1f);
                Assert.Equal(1f, m.Colors[i + 3]);
            }
        }

        // Animated mobies — retail MobySequence playback. Oozla places 2
        // instances of the single-joint spinner oc1134 (170-frame sequence).
        Assert.NotNull(world.AnimatedMeshes);
        Assert.Equal(2, world.AnimatedMeshes!.Count);
        foreach (var am in world.AnimatedMeshes)
        {
            Assert.Equal("moby", am.AssetKind);
            Assert.Equal(170, am.Frames.Count);
            Assert.Equal(91, am.TriangleCount);
            Assert.All(am.Frames, f => Assert.Equal(am.VertexCount * 3, f.Length));
            Assert.Equal(am.VertexCount * 4, am.Colors.Length);
        }

        // Textures — the Checkpoint E render inputs, keyed (AssetKind, TextureId).
        Assert.Equal(77 + 79 + 38 + 199 + 3, world.Textures.Count);
        Assert.Equal(77, world.Textures.Count(t => t.AssetKind == "tfrag"));
        Assert.Equal(3, world.Textures.Count(t => t.AssetKind == "sky"));
        Assert.All(world.Textures, t => Assert.Equal(t.Width * t.Height * 4, t.Rgba.Length));
        // Every textured render mesh resolves to a decoded texture.
        var texKeys = world.Textures.Select(t => (t.AssetKind, t.TextureId)).ToHashSet();
        foreach (var m in world.Meshes.Where(m => m.AssetKind is "tfrag" or "tie" or "shrub" or "moby" && m.TextureId >= 0))
        {
            Assert.Contains((m.AssetKind, m.TextureId), texKeys);
        }
    }

    [SkippableFact]
    public void Level0_AranosWorldImportMatchesTheBaseline()
    {
        // LEVEL0 is un-chunked (lumps 0-3 only): tfrags + collision come from the
        // level core, not chunk lumps. Exercises that path of GcWorldImport.
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");

        using var reader = new FileRandomAccessReader(iso!);
        var world = GcWorldImport.Build(reader, 0);

        int Tris(string kind) => world.Meshes.Where(m => m.AssetKind == kind).Sum(m => m.TriangleCount);
        Assert.Equal(264, world.Meshes.Count);
        Assert.Equal(33_710, Tris("tfrag"));
        Assert.Equal(771_104, Tris("tie"));
        Assert.Equal(71_334, Tris("shrub"));
        // oc2602 instances (200-frame spin) are lifted out as animated mobies;
        // class 500 is now preserved separately as dynamic objects.
        Assert.Equal(224_801, Tris("moby"));
        Assert.Equal(4_859, world.TotalDynamicTriangles);
        Assert.Equal(43, world.DynamicObjects!.Count(o => o.NativeClassId == 500));
        Assert.NotEmpty(world.AnimatedMeshes!);
        Assert.All(world.AnimatedMeshes!, a => Assert.Equal(200, a.Frames.Count));

        Assert.Single(world.CollisionMeshes);
        Assert.Equal((12_079, 81_113), (world.CollisionMeshes[0].Octants, world.CollisionMeshes[0].Triangles));
        Assert.Equal(world.CollisionMeshes[0].Triangles * 3, world.CollisionMeshes[0].Indices.Length);

        Assert.NotNull(world.Environment);
        Assert.Equal(0f, world.Environment!.DeathHeight);
    }
    private static uint Word(byte[] bytes)
    {
        if (bytes.Length != sizeof(uint))
        {
            throw new ArgumentException("Expected exactly one 32-bit word.", nameof(bytes));
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    private static uint[] Words(byte[] bytes)
    {
        if ((bytes.Length & 3) != 0)
        {
            throw new ArgumentException("Expected a whole number of 32-bit words.", nameof(bytes));
        }

        var words = new uint[bytes.Length / sizeof(uint)];
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * sizeof(uint), sizeof(uint)));
        }

        return words;
    }

    /// <summary>Stable digest of a set of class meshes ÔÇö mirrors the equivalence script run against reference-ts.</summary>
    private static string HashClassMeshes(IEnumerable<(int OClass, int[] Indices, double[] Positions, int[] MaterialSlots, int[] TriTexIds)> classes)
    {
        using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        var scratch = new byte[4];
        void I32(int v) { BinaryPrimitives.WriteInt32LittleEndian(scratch, v); sha.AppendData(scratch); }

        foreach (var (oClass, indices, positions, materialSlots, triTexIds) in classes)
        {
            I32(oClass);
            I32(indices.Length / 3);
            I32(positions.Length / 3);
            foreach (var p in positions) I32((int)System.Math.Floor(p * 10000 + 0.5)); // match JS Math.round (half toward +inf)
            foreach (var i in indices) I32(i);
            foreach (var s in materialSlots) I32(s);
            foreach (var id in triTexIds) I32(id);
        }

        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }
}
