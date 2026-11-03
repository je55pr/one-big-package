using System.Buffers.Binary;
using OBP.RAC2.Geometry;
using OBP.RAC2.Level;
using static OBP.Tests.Rac2RetailLevelFixtures;

namespace OBP.Tests;

/// <summary>
/// Oozla (LEVEL1) decoded-asset digests: textures, TIE and shrub classes, sky, Moby classes and Moby sequences
/// must match the TypeScript reference. Retail-gated on <c>OBP_GC_ISO</c>.
/// </summary>
public class Rac2LevelDecodedAssetTests
{
    [SkippableTheory]
    [InlineData(Rac2LevelTextures.Table.Tfrag, 77, "ce547004b8eeb754e9baae636ecc58da5d22ba100eaa85e25caf432b48622913")]
    [InlineData(Rac2LevelTextures.Table.Tie, 79, "9542d4d0bb1c0b7749297b7c9db8453131dac257204f54f7e77a9b828d5ac5b2")]
    [InlineData(Rac2LevelTextures.Table.Shrub, 38, "d95521faf7fed21b18159f7986e2784d0148e6949a794b31001048b36fde4cf6")]
    [InlineData(Rac2LevelTextures.Table.Moby, 199, "ed5fca629a9ff7d0448241ea18e9b4a24be426d6b50d01d3e62649757c9d2aa2")]
    public void Level1_DecodedTexturesMatchTypeScriptPixels(Rac2LevelTextures.Table table, int count, string expectedSha256)
    {
        var iso = RequireIso();

        var (wad, header) = OpenLevelWad(iso!, 1);
        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        var textures = Rac2LevelTextures.Read(core, table);

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
        var iso = RequireIso();

        var (wad, header) = OpenLevelWad(iso!, 1);
        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        var classes = Rac2Tie.ReadClasses(core);

        Assert.Equal(91, classes.Count);
        var sha = HashClassMeshes(classes.OrderBy(kv => kv.Key)
            .Select(kv => (kv.Key, kv.Value.Mesh.Indices, kv.Value.Mesh.Positions, kv.Value.Mesh.TriangleMaterialSlots, kv.Value.TriangleTextureIds)));
        Assert.Equal("826999b974d58093089e823d84bf2025618aafedd2d8a6877d476abdd66b9247", sha);
    }

    [SkippableFact]
    public void Level1_ShrubClassesHavePinnedCorrectedStripWinding()
    {
        var iso = RequireIso();

        var (wad, header) = OpenLevelWad(iso!, 1);
        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        var classes = Rac2Shrub.ReadClasses(core);

        Assert.Equal(24, classes.Count);
        var sha = HashClassMeshes(classes.OrderBy(kv => kv.Key)
            .Select(kv => (kv.Key, kv.Value.Mesh.Indices, kv.Value.Mesh.Positions, kv.Value.Mesh.TriangleMaterialSlots, kv.Value.TriangleTextureIds)));
        // The earlier TypeScript parity bug produced fffd756...; this SHA pins
        // corrected GIF-strip winding without changing positions/materials.
        Assert.Equal("42f20d3041c86ba68e99493e03f4dbdf73ccd0126a7fa676c99fb8d2e5349b29", sha);
    }

    [SkippableFact]
    public void Level1_SkyMatchesTypeScript()
    {
        var iso = RequireIso();

        var (wad, header) = OpenLevelWad(iso!, 1);
        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        var sky = Rac2Sky.ReadLevelSky(core) ?? throw new InvalidDataException("Oozla has a sky section.");

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
        var iso = RequireIso();

        var (wad, header) = OpenLevelWad(iso!, 1);
        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        var classes = Rac2MobyClasses.Read(core);

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
        var iso = RequireIso();

        var (wad, header) = OpenLevelWad(iso!, 1);
        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        var classes = Rac2MobyClasses.Read(core);

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
