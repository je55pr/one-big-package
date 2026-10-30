using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OBP.IO;
using OBP.PS2.Geometry;
using OBP.PS2.Iso;
using OBP.RAC2.Level;
using OBP.RAC3.Level;

namespace OBP.Tests;

/// <summary>
/// Exact decoder output signatures, pinned before the #488 stage extraction.
/// No retail payloads are stored in the repository.
/// </summary>
public sealed class GcUyaMobyDecodeCharacterizationTests
{
    private static byte[] PacketClass()
    {
        var source = new byte[0x140];
        BinaryPrimitives.WriteInt32LittleEndian(source, 0x50);
        source[4] = 2;
        BinaryPrimitives.WriteSingleLittleEndian(source.AsSpan(0x24), 1024);
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(0x80), 0x65020000);
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(0x8c), 0x62080000);
        for (int packet = 0; packet < 2; packet++)
        {
            int entry = 0x50 + packet * 0x10, vh = 0xa0 + packet * 0x60;
            BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(entry), 0x80);
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(entry + 4), 2);
            BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(entry + 8), vh);
            source[entry + 12] = 4;
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(vh + 6), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(vh + 8), (ushort)packet);
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(vh + 12), 0x20);
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(vh + 16), 17 << 7);
            for (int axis = 0; axis < 3; axis++)
                BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(vh + 0x2a + axis * 2),
                    (short)(4 + packet * 3 + axis));
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(vh + 0x3e),
                (ushort)(packet == 0 ? 17 : 31));
        }
        return source;
    }

    private static byte[] SkinClass()
    {
        var source = new byte[0x200];
        PacketClass().CopyTo(source, 0);
        source[8] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(0x14), 0x140);
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(0x18), 0x180);
        for (int axis = 0; axis < 3; axis++)
            BinaryPrimitives.WriteSingleLittleEndian(source.AsSpan(0x140 + axis * 20), 1);
        for (int packet = 0; packet < 2; packet++)
        {
            int vh = 0xa0 + packet * 0x60;
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(vh + 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(vh + 6), 0);
            source[vh + 0x24] = 128;
            source[vh + 0x25] = 128;
            source[vh + 0x27] = 4;
        }
        return source;
    }

    // Fixed binary layout, including the exact float/double bit patterns.
    // Material records are serialized in declared property order.
    private static string Fingerprint(GcUyaMoby.Mesh mesh)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            void Doubles(double[] values)
            {
                writer.Write(values.Length);
                foreach (double value in values)
                    writer.Write(BitConverter.DoubleToInt64Bits(value));
            }
            void Floats(float[] values)
            {
                writer.Write(values.Length);
                foreach (float value in values)
                    writer.Write(BitConverter.SingleToInt32Bits(value));
            }
            void Ints(int[] values)
            {
                writer.Write(values.Length);
                foreach (int value in values) writer.Write(value);
            }
            Doubles(mesh.Positions);
            Floats(mesh.Uvs);
            Ints(mesh.Indices);
            Ints(mesh.TriangleMaterialSlots);
            writer.Write(BitConverter.SingleToInt32Bits(mesh.Scale));
            writer.Write(mesh.Skinned);
            writer.Write(mesh.SkinningApplied);
            Floats(mesh.Normals);
            Ints(mesh.VertexJoints);
            Floats(mesh.VertexWeights);
            Doubles(mesh.SkinLocalPositions);
            writer.Write(mesh.SkinStateFullyResolved);
            writer.Write(mesh.NativeInitialPoseApplied);
            writer.Write(mesh.Materials.Count);
            foreach (var material in mesh.Materials)
                writer.Write(JsonSerializer.Serialize(material));
            Ints(mesh.TriangleMaterialStateIndices);
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    [Theory]
    [InlineData(false, false, "A0865F60AAD1E9F17FEC80B9C1ACCD08D6100F4C2A59ED37B18E00BC46DA4D46")]
    [InlineData(false, true, "D30C1622A4ABB4BB53C523ADF79AC910EC671D49A7C8A984D7246E2E24162704")]
    [InlineData(true, false, "82E7DFC0EC0B0BB503692F290C6EDF9EA555A222FC6FAC4EB6F371428125DBA9")]
    [InlineData(true, true, "326D49632F99E2AFE7D1011AEFDB0BF4EC0FAC93EB8ACBCC679E5318D8C5E935")]
    public void SyntheticDecoderOutputIsPinned(bool skin, bool recover, string expected)
    {
        var mesh = GcUyaMoby.ReadClass(skin ? SkinClass() : PacketClass(), recover);
        string actual = Fingerprint(mesh);
        Assert.True(expected == actual, $"Expected {expected}, actual {actual}");
    }


    // Two packets: packet 0 selects an explicit GS material; packet 1 emits
    // another strip with no AD-GIF and must inherit its material/state index.
    private static byte[] MaterialPacketClass()
    {
        var source = new byte[0x420];
        BinaryPrimitives.WriteInt32LittleEndian(source, 0x50);
        source[4] = 2;
        BinaryPrimitives.WriteSingleLittleEndian(source.AsSpan(0x24), 1024f);
        for (int packet = 0; packet < 2; packet++)
        {
            int entry = 0x50 + packet * 0x10;
            int vif = packet == 0 ? 0x100 : 0x180;
            int vh = packet == 0 ? 0x220 : 0x320;
            BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(entry), vif);
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(entry + 4),
                (ushort)(packet == 0 ? 7 : 3));
            BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(entry + 8), vh);
            source[entry + 12] = 6; // Header 2 + vertices 3 + epilogue 1.
            BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(vif), 0x65030000);
            for (int vertex = 0; vertex < 3; vertex++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(vif + 4 + vertex * 4),
                    (short)(vertex * 100));
                BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(vif + 6 + vertex * 4),
                    (short)(packet * 200));
            }
            BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(vif + 16), 0x62100000);
            source[vif + 22] = 1; // Switch to texture[0] at first zero index.
            byte[] strip = packet == 0
                ? [0, 128, 1, 2, 3, 1, 2, 3, 1, 2, 3, 0]
                : [128, 128, 1, 2, 3, 1, 2, 3, 1, 2, 3, 0];
            strip.CopyTo(source, vif + 24);
            if (packet == 0)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(vif + 36), 0x6e100000);
                BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(vif + 40 + 0x20), 9);
            }
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(vh + 6), 3);
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(vh + 12), 0x20);
            int vbase = vh + 0x20;
            for (int v = 0; v < 3; v++)
            {
                int at = vbase + v * 0x10;
                BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(at + 0x0a),
                    (short)(v == 1 ? 16 : 0));
                BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(at + 0x0c),
                    (short)(v == 2 ? 16 : 0));
                BinaryPrimitives.WriteInt16LittleEndian(source.AsSpan(at + 0x0e),
                    (short)(packet * 16));
                BinaryPrimitives.WriteUInt16LittleEndian(
                    source.AsSpan(vbase + 3 * 16 + 4 + (v + 3) * 2), (ushort)(packet * 10 + v));
            }
        }
        return source;
    }

    [Theory]
    [InlineData(false, "A8E7E60B6DA0D57E0E90B18115DA489951AC9F513FF785AEE2148F119EF4D4B3")]
    [InlineData(true, "A8E7E60B6DA0D57E0E90B18115DA489951AC9F513FF785AEE2148F119EF4D4B3")]
    public void SyntheticMaterialStateIsInheritedAcrossPackets(bool recover, string expected)
    {
        var mesh = GcUyaMoby.ReadClass(MaterialPacketClass(), recover);
        Assert.NotEmpty(mesh.Indices);
        var inheritedTriangles = Enumerable.Range(0, mesh.Indices.Length / 3)
            .Where(t => mesh.Indices.Skip(t * 3).Take(3).All(vertex => vertex >= 3))
            .ToArray();
        Assert.NotEmpty(inheritedTriangles);
        Assert.All(inheritedTriangles, t =>
        {
            Assert.Equal(9, mesh.TriangleMaterialSlots[t]);
            Assert.Equal(0, mesh.TriangleMaterialStateIndices[t]);
        });
        Assert.Single(mesh.Materials);
        Assert.Equal(9, mesh.Materials[0].TextureId);
        Assert.All(mesh.TriangleMaterialSlots, id => Assert.Equal(9, id));
        Assert.All(mesh.TriangleMaterialStateIndices, id => Assert.Equal(0, id));
        string actual = Fingerprint(mesh);
        Assert.True(expected == actual, $"Expected {expected}, actual {actual}");
    }


    // A real triangle-bearing packet, rather than the empty strips used by
    // cache tests, keeps ReadClass on the native skin path for validation.
    private static byte[] SkinnedMaterialPacketClass()
    {
        var source = new byte[0x600];
        MaterialPacketClass().CopyTo(source, 0);
        source[8] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(0x14), 0x440);
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(0x18), 0x4c0);
        for (int packet = 0; packet < 2; packet++)
        {
            int vh = packet == 0 ? 0x220 : 0x320;
            int vbase = vh + 0x20;
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(vh + 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(vh + 6), 2);
            source[vbase + 4] = 128;
            source[vbase + 5] = 128;
        }
        return source;
    }

    [Theory]
    [InlineData(0, 0, true)]     // Both source slots are valid.
    [InlineData(2, 3, false)]    // Native matrix addresses require alignment.
    [InlineData(2, 12, false)]   // Referenced matrix slot was never written.
    [InlineData(4, 127, false)]  // Two-weight sum is not 256.
    [InlineData(1, 2, false)]    // Current-record upper bits select absent joint 1.
    public void NativeSkinValidationFailsClosedWithoutLosingDrawableGeometry(
        int byteOffset, byte value, bool expectedResolved)
    {
        var source = SkinnedMaterialPacketClass();
        if (byteOffset != 0) source[0x240 + byteOffset] = value;
        var mesh = GcUyaMoby.ReadClass(source, recoverPersistentVertexCache: true);
        Assert.True(mesh.Skinned);
        Assert.True(mesh.SkinningApplied);
        Assert.NotEmpty(mesh.Indices);
        Assert.Equal(expectedResolved, mesh.SkinStateFullyResolved);
        Assert.Equal(mesh.Positions.Length, mesh.SkinLocalPositions.Length);
        Assert.Equal(mesh.Positions.Length, mesh.VertexWeights.Length);
    }

    [Fact]
    public void TwoPacketMaterialFixturePreservesAuthoredWindingAndDegenerateSkips()
    {
        var mesh = GcUyaMoby.ReadClass(MaterialPacketClass(), recoverPersistentVertexCache: true);
        // This explicit vertex-index expectation protects the strip output
        // independently of the whole-mesh SHA-256 fingerprint.
        Assert.Equal(new[]
        {
            1, 0, 2, 1, 2, 0, 0, 2, 1, 0, 1, 2,
            4, 3, 5, 4, 5, 3, 3, 5, 4, 3, 4, 5, 5, 4, 3, 5, 3, 4, 4, 3, 5,
        }, mesh.Indices);
    }

    private static string RequireRetailIso(string name)
    {
        string? iso = Environment.GetEnvironmentVariable(name);
        Skip.If(string.IsNullOrWhiteSpace(iso), $"{name} missing");
        return iso!;
    }

    [SkippableTheory]
    [InlineData(2466, "C8B409F9030096B74C6244CB59E9370BC05AD481A7ACCCE90DEF08B2C4E5A631")]
    [InlineData(2859, "05E6DFEA55DA003C907E85100A12E4770C540D771F0F2732DB8FA74850EEE47C")]
    public void GcRetailDecodedMeshIsPinned(int classId, string expected)
    {
        using var reader = new FileRandomAccessReader(RequireRetailIso("OBP_GC_ISO"));
        var fs = Iso9660Filesystem.Open(reader);
        var wad = fs.OpenFile("/G/LEVEL1.WAD")!;
        var header = Rac2LevelWad.ReadHeader(wad);
        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        var classes = Rac2MobyClasses.Read(core, level: 1);
        Assert.True(classes.TryGetValue(classId, out var moby));
        string actual = Fingerprint(moby!.Mesh);
        Assert.True(expected == actual, $"Expected {expected}, actual {actual}");
    }

    [SkippableTheory]
    [InlineData(6836, "D854D9460474811C6730A04ED97C2C26AF3B7DB7B885D24EBCECD629C6E2F3DA")]
    [InlineData(5821, "F84C27696651DD102524FA7FDB6E59AD6BDDB7EE731F9E393CA19F56AF6709BF")]
    public void UyaRetailDecodedMeshIsPinned(int classId, string expected)
    {
        using var reader = new FileRandomAccessReader(RequireRetailIso("OBP_UYA_ISO"));
        var core = Rac3LevelCore.Open(reader, 1).Core;
        int textureCount = OBP.RAC3.Level.Rac3Assets.ReadTextures(
            core, OBP.RAC3.Level.Rac3Assets.TextureTable.Moby).Count;
        var classes = OBP.RAC3.Level.Rac3Assets.ReadMobyClasses(core, textureCount,
            nativePoseOverride: true).Decoded;
        Assert.True(classes.TryGetValue(classId, out var moby));
        string actual = Fingerprint(moby!.Mesh);
        Assert.True(expected == actual, $"Expected {expected}, actual {actual}");
    }
}
