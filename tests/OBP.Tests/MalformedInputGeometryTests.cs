using System.Buffers.Binary;
using OBP.PS2.Animation;
using OBP.PS2.Collision;
using OBP.PS2.Geometry;
using OBP.PS2.Textures;

namespace OBP.Tests;

/// <summary>
/// Truncated, zero-length, oversized-length and misaligned synthetic inputs for
/// the shared PS2 geometry, collision, texture, skeleton and animation readers.
/// All fixtures are generated in memory; no retail bytes are involved.
/// </summary>
public sealed class MalformedInputGeometryTests
{
    private static void I32(byte[] b, int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at), value);
    private static void U32(byte[] b, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), value);
    private static void U16(byte[] b, int at, int value) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), (ushort)value);
    private static void F32(byte[] b, int at, float value) => BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(at), value);

    private static readonly int[] HostileOffsets = [int.MaxValue, int.MaxValue - 1, int.MaxValue - 0x0f, int.MaxValue - 0x2b, -1, int.MinValue];

    /// <summary>
    /// Run <paramref name="read"/> over deterministic pseudo-random buffers whose words are biased
    /// towards small offsets/counts so parsers get past their first validation. Parsing may succeed
    /// or fail, but may only fail with <see cref="InvalidDataException"/>.
    /// </summary>
    private static void FuzzOnlyInvalidData(string label, int seed, int iterations, int minLength, int maxLength, Action<byte[]> read)
    {
        var random = new Random(seed);
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            var buffer = new byte[random.Next(minLength, maxLength + 1)];
            for (int at = 0; at + 4 <= buffer.Length; at += 4)
            {
                int mode = random.Next(10);
                uint word = mode switch
                {
                    < 4 => 0u,
                    < 8 => (uint)random.Next(0, 0x200),
                    8 => (uint)random.NextInt64(0, 1L << 32),
                    _ => (uint)-random.Next(1, 257),
                };
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(at), word);
            }

            var ex = Record.Exception(() => read(buffer));
            Assert.True(ex is null or InvalidDataException,
                $"{label} iteration {iteration} (seed {seed}, {buffer.Length} bytes): {ex?.GetType().Name}: {ex?.Message}");
        }
    }

    private static void SweepPrefixesAndBytes(string label, byte[] valid, Action<byte[]> read, params Type[] extraAllowed)
    {
        bool Allowed(Exception? ex) => ex is null or InvalidDataException || extraAllowed.Any(t => t.IsInstanceOfType(ex));
        Assert.Null(Record.Exception(() => read(valid)));
        for (int length = 0; length < valid.Length; length++)
        {
            var ex = Record.Exception(() => read(valid[..length]));
            Assert.True(Allowed(ex), $"{label}: truncation to {length}: {ex?.GetType().Name}: {ex?.Message}");
        }

        for (int at = 0; at < valid.Length; at++)
        {
            foreach (byte value in new byte[] { 0x00, 0x01, 0x7f, 0x80, 0xff })
            {
                byte[] copy = (byte[])valid.Clone();
                copy[at] = value;
                var ex = Record.Exception(() => read(copy));
                Assert.True(Allowed(ex), $"{label}: byte {at}=0x{value:x2}: {ex?.GetType().Name}: {ex?.Message}");
            }
        }
    }

    // ------------------------------------------------------- collision ----

    private static byte[] ValidCollision()
    {
        var b = new byte[64];
        I32(b, 0, 8);          // mesh offset
        I32(b, 4, 0);          // no hero groups
        U16(b, 8, 0);          // z coord
        U16(b, 10, 1);         // z count
        U16(b, 12, 2);         // z node at 8 + 2*4
        U16(b, 16, 0);         // y coord
        U16(b, 18, 1);         // y count
        U32(b, 20, 20);        // y node at 8 + 20
        U16(b, 28, 0);         // x coord
        U16(b, 30, 1);         // x count
        U32(b, 32, 36u << 8);  // octant at 8 + 36
        U16(b, 44, 1);         // faces
        b[46] = 3;             // vertices
        b[47] = 0;             // quads
        b[60] = 0; b[61] = 1; b[62] = 2; b[63] = 5;
        return b;
    }

    [Fact]
    public void CollisionDecodesTheMinimalOctree()
    {
        var mesh = RcCollision.Read(ValidCollision());
        Assert.Single(mesh.Triangles);
        Assert.Equal(5, mesh.Triangles[0].MaterialId);
        Assert.Equal(9, mesh.Positions.Length);
    }

    [Fact]
    public void CollisionHeaderOffsetsAreValidated()
    {
        Assert.Throws<InvalidDataException>(() => RcCollision.Read([]));
        Assert.Throws<InvalidDataException>(() => RcCollision.Read(new byte[7]));
        foreach (int meshOffset in new[] { 0, 7, 64, 1000, -1, int.MinValue, int.MaxValue })
        {
            byte[] b = ValidCollision();
            I32(b, 0, meshOffset);
            Assert.Throws<InvalidDataException>(() => RcCollision.Read(b));
        }

        foreach (int hero in new[] { 4, 65, -1, int.MaxValue })
        {
            byte[] b = ValidCollision();
            I32(b, 4, hero);
            Assert.Throws<InvalidDataException>(() => RcCollision.Read(b));
        }
    }

    [Theory]
    [InlineData(uint.MaxValue)]
    [InlineData(0x8000_0000u)]
    [InlineData(0x7fff_ffffu)]
    [InlineData(0x7fff_fff8u)]
    [InlineData(64u)]
    public void CollisionYNodeOffsetsCannotWrapOrEscape(uint yByte)
    {
        byte[] b = ValidCollision();
        U32(b, 20, yByte);
        Assert.Throws<InvalidDataException>(() => RcCollision.Read(b));
    }

    [Fact]
    public void CollisionNodeOffsetsAtTheEdgeOfIntRangeAreRejectedAsInvalidData()
    {
        foreach (uint z in new uint[] { 0xffff, 0x3fff })
        {
            byte[] b = ValidCollision();
            U16(b, 12, (int)z);
            Assert.Throws<InvalidDataException>(() => RcCollision.Read(b));
        }

        byte[] octant = ValidCollision();
        U32(octant, 32, 0xffffff00u);
        Assert.Throws<InvalidDataException>(() => RcCollision.Read(octant));

        byte[] counts = ValidCollision();
        U16(counts, 10, 0xffff);
        Assert.Throws<InvalidDataException>(() => RcCollision.Read(counts));
    }

    [Fact]
    public void CollisionSafetyCapsAndFaceIndicesAreEnforced()
    {
        byte[] b = ValidCollision();
        Assert.Throws<InvalidDataException>(() => RcCollision.Read(b, new RcCollision.Options(MaxOctants: 0)));
        Assert.Throws<InvalidDataException>(() => RcCollision.Read(b, new RcCollision.Options(MaxVertices: 2)));
        Assert.Throws<InvalidDataException>(() => RcCollision.Read(b, new RcCollision.Options(MaxTriangles: 0)));
        Assert.Throws<InvalidDataException>(() => RcCollision.Read(b, new RcCollision.Options(MaxGridCells: 2)));
        Assert.Single(RcCollision.Read(b, new RcCollision.Options(MaxGridCells: 3)).Triangles);

        byte[] badFace = ValidCollision();
        badFace[62] = 3; // vertex index == vertex count
        Assert.Throws<InvalidDataException>(() => RcCollision.Read(badFace));

        byte[] badQuads = ValidCollision();
        badQuads[47] = 2; // more quads than faces
        Assert.Throws<InvalidDataException>(() => RcCollision.Read(badQuads));

        byte[] hugeFaces = ValidCollision();
        U16(hugeFaces, 44, 0xffff);
        Assert.Throws<InvalidDataException>(() => RcCollision.Read(hugeFaces));
    }

    [Fact]
    public async Task CollisionOctreeWithSharedNodesCannotMultiplyTheWalk()
    {
        // One z-slot -> a y-node with 65535 slots that all point at the same x-node,
        // which itself has 65535 empty slots: ~4.3e9 slot visits from ~512 KiB of input.
        const int yNode = 8 + 8;
        const int xNode = yNode + 4 + 65535 * 4;
        var b = new byte[xNode + 4 + 65535 * 4];
        I32(b, 0, 8);
        U16(b, 10, 1);
        U16(b, 12, 2);
        U16(b, yNode + 2, 0xffff);
        for (int y = 0; y < 65535; y++)
        {
            U32(b, yNode + 4 + y * 4, xNode - 8);
        }

        U16(b, xNode + 2, 0xffff);

        var task = Task.Run(() => Record.Exception(() => RcCollision.Read(b)));
        var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)));
        Assert.True(ReferenceEquals(finished, task), "RcCollision.Read did not terminate on a shared-node octree.");
        Assert.IsType<InvalidDataException>(await task);
    }

    [Fact]
    public void CollisionSweepsOnlyFailWithInvalidData()
    {
        SweepPrefixesAndBytes("collision", ValidCollision(), b => RcCollision.Read(b));
        FuzzOnlyInvalidData("collision-fuzz", 11, 3000, 8, 256, b => RcCollision.Read(b));
    }

    [Fact]
    public void PaletteSwizzleRejectsShortInputsWithArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Ps2Texture.SwizzlePalette(new uint[255]));
        Assert.Equal(256, Ps2Texture.SwizzlePalette(new uint[256]).Length);
    }

    // -------------------------------------------------------------- sky ----

    private static byte[] ValidSky()
    {
        var b = new byte[0xa0];
        b[0] = 10; b[1] = 20; b[2] = 30; b[3] = 0x80;
        U16(b, 6, 1);          // one shell
        I32(b, 0x20, 0x40);
        I32(b, 0x40, 1);       // one cluster
        I32(b, 0x40 + 4, 0);   // textured
        int ch = 0x50;
        I32(b, ch + 0x10, 0x70);
        U16(b, ch + 0x14, 3);
        U16(b, ch + 0x16, 1);
        U16(b, ch + 0x18, 0);
        U16(b, ch + 0x1a, 24);
        U16(b, ch + 0x1c, 36);
        b[0x70 + 36 + 0] = 0; b[0x70 + 36 + 1] = 1; b[0x70 + 36 + 2] = 2; b[0x70 + 36 + 3] = 0xff;
        return b;
    }

    [Fact]
    public void SkyDecodesAMinimalShell()
    {
        var sky = RcSky.Read(ValidSky());
        var shell = Assert.Single(sky.Shells);
        Assert.Equal(new[] { 2, 1, 0 }, shell.Indices);
        Assert.Equal(-1, Assert.Single(shell.TriangleTextureIds));
    }

    [Fact]
    public void SkyHeaderAndShellCountsAreValidated()
    {
        Assert.Throws<InvalidDataException>(() => RcSky.Read([]));
        Assert.Throws<InvalidDataException>(() => RcSky.Read(new byte[RcSky.HeaderSize - 1]));
        Assert.Empty(RcSky.Read(new byte[RcSky.HeaderSize]).Shells);
        foreach (int count in new[] { 9, -1, short.MaxValue, short.MinValue })
        {
            byte[] b = new byte[RcSky.HeaderSize];
            BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(6), (short)count);
            Assert.Throws<InvalidDataException>(() => RcSky.Read(b));
        }
    }

    [Fact]
    public void SkyShellOffsetsCannotOverflowTheBoundsCheck()
    {
        foreach (int offset in HostileOffsets.Concat(new[] { 0, 0x9f, 0xa0, 0x100 }))
        {
            byte[] b = ValidSky();
            I32(b, 0x20, offset);
            Assert.Empty(RcSky.Read(b).Shells);
        }
    }

    [Fact]
    public void SkyClusterDataOffsetsCannotOverflowTheBoundsCheck()
    {
        foreach (int data in HostileOffsets.Concat(new[] { 0, 0x9f, 0xa0 }))
        {
            byte[] b = ValidSky();
            I32(b, 0x50 + 0x10, data);
            Assert.Empty(RcSky.Read(b).Shells);
        }

        foreach (int offset in new[] { 0xffff, 0x8000 })
        {
            byte[] b = ValidSky();
            U16(b, 0x50 + 0x18, offset);
            Assert.Empty(RcSky.Read(b).Shells);
        }
    }

    [Fact]
    public void SkyTextureTablesCannotOverflowTheBoundsChecks()
    {
        foreach (int defs in HostileOffsets.Where(o => o > 0).Concat(new[] { 0x9f, 0xa0, 0x1000 }))
        {
            byte[] b = ValidSky();
            U16(b, 0x0c, 4);
            I32(b, 0x10, defs);
            I32(b, 0x14, 0x40);
            Assert.Empty(RcSky.Read(b).Textures);
        }

        // Palette and pixel offsets that sum to exactly int.MaxValue / past it.
        foreach (int paletteOffset in new[] { int.MaxValue - 0x40, int.MaxValue - 0x3f, int.MaxValue - 0x20, int.MaxValue })
        {
            var b = new byte[0x60];
            U16(b, 0x0c, 1);
            I32(b, 0x10, 0x40);   // texture defs
            I32(b, 0x14, 0x40);   // data base
            I32(b, 0x40, paletteOffset);
            I32(b, 0x44, 0);      // pixel offset
            I32(b, 0x48, 1);
            I32(b, 0x4c, 1);
            Assert.Empty(RcSky.Read(b).Textures);
        }

        var pixelOverflow = new byte[0x60];
        U16(pixelOverflow, 0x0c, 1);
        I32(pixelOverflow, 0x10, 0x40);
        I32(pixelOverflow, 0x14, 0x40);
        I32(pixelOverflow, 0x40, 0);
        I32(pixelOverflow, 0x44, int.MaxValue - 0x40);
        I32(pixelOverflow, 0x48, 1024);
        I32(pixelOverflow, 0x4c, 1024);
        Assert.Empty(RcSky.Read(pixelOverflow).Textures);
    }

    [Fact]
    public void SkySweepsOnlyFailWithInvalidData()
    {
        SweepPrefixesAndBytes("sky", ValidSky(), b => RcSky.Read(b));
        FuzzOnlyInvalidData("sky-fuzz", 12, 3000, 0x40, 0x180, b => RcSky.Read(b));
    }

    // ------------------------------------------------------ shrub / tie ----

    private static byte[] ShrubWithFirstUnpackOfTinyPayload()
    {
        var b = new byte[0x48 + 24];
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(0x28), 1);
        I32(b, 0x40, 0x48);
        I32(b, 0x44, 24);
        for (int i = 0; i < 3; i++)
        {
            U32(b, 0x48 + i * 8, 0x6201_0000u); // UNPACK S8 x1 -> one payload byte, padded to a word
        }

        return b;
    }

    [Fact]
    public void ShrubSkipsPacketsWhoseHeaderUnpackIsTooSmall()
    {
        var mesh = RcShrub.ReadClass(ShrubWithFirstUnpackOfTinyPayload());
        Assert.Empty(mesh.Indices);
    }

    [Fact]
    public void ShrubPacketRangesCannotOverflowTheBoundsCheck()
    {
        Assert.Throws<InvalidDataException>(() => RcShrub.ReadClass([]));
        Assert.Throws<InvalidDataException>(() => RcShrub.ReadClass(new byte[RcShrub.ClassHeaderSize - 1]));
        foreach (var (offset, size) in new[] { (int.MaxValue, 1), (1, int.MaxValue), (int.MaxValue, int.MaxValue), (0x48, int.MaxValue), (-1, 8), (0x48, -8), (0, 0) })
        {
            byte[] b = ShrubWithFirstUnpackOfTinyPayload();
            I32(b, 0x40, offset);
            I32(b, 0x44, size);
            Assert.Empty(RcShrub.ReadClass(b).Indices);
        }

        // A packet table larger than the buffer stops at the end rather than reading past it.
        byte[] many = ShrubWithFirstUnpackOfTinyPayload();
        BinaryPrimitives.WriteInt16LittleEndian(many.AsSpan(0x28), short.MaxValue);
        Assert.Empty(RcShrub.ReadClass(many).Indices);
    }

    [Fact]
    public void ShrubAndTieFuzzOnlyFailWithInvalidData()
    {
        FuzzOnlyInvalidData("shrub-fuzz", 21, 3000, 0x40, 0x200, b => RcShrub.ReadClass(b));
        FuzzOnlyInvalidData("tie1-fuzz", 22, 3000, 0x70, 0x300, b => RcTie.ReadClass(b, RcTie.Rac1Layout));
        FuzzOnlyInvalidData("tie2-fuzz", 23, 3000, 0x80, 0x300, b => RcTie.ReadClass(b, RcTie.Rac2Layout));
    }

    [Fact]
    public void TiePacketOffsetsCannotOverflowTheBoundsCheck()
    {
        Assert.Throws<InvalidDataException>(() => RcTie.ReadClass([], RcTie.Rac1Layout));
        Assert.Throws<InvalidDataException>(() => RcTie.ReadClass(new byte[RcTie.Rac1ClassHeaderSize - 1], RcTie.Rac1Layout));
        Assert.Throws<InvalidDataException>(() => RcTie.ReadClass(new byte[RcTie.Rac2ClassHeaderSize - 1], RcTie.Rac2Layout));

        foreach (int packetOffset in new[] { int.MaxValue - 0x70, int.MaxValue - 0x70 - 0x2a, int.MaxValue, int.MinValue, -0x70, 0, 0x7fffffff - 0x80 })
        {
            var b = new byte[0x80];
            I32(b, 0, 0x70);
            b[0x20] = 1;                 // packet count
            I32(b, 0x70, packetOffset);  // packet offset relative to the table
            Assert.Empty(RcTie.ReadClass(b, RcTie.Rac1Layout).Indices);
        }

        foreach (int adGif in new[] { int.MaxValue, int.MaxValue - 0x4f, -1, 0, 0x7f })
        {
            var b = new byte[0x80];
            b[0x23] = 2;                 // texture count
            I32(b, 0x2c, adGif);
            Assert.Throws<InvalidDataException>(() => RcTie.ReadClass(b, RcTie.Rac1Layout));
        }
    }

    // ------------------------------------------------------------ tfrag ----

    [Fact]
    public void TfragHeaderAndTableAreValidated()
    {
        Assert.Throws<InvalidDataException>(() => RcTfrag.Read([]));
        Assert.Throws<InvalidDataException>(() => RcTfrag.Read(new byte[15]));
        foreach (var (table, count) in new[] { (0, 0), (0x0f, 0), (0x11, 0), (0x10, 1), (0x10, int.MaxValue), (0x10, -1), (int.MaxValue, 0), (-1, 0) })
        {
            var b = new byte[0x10];
            I32(b, 0, table);
            I32(b, 4, count);
            Assert.Throws<InvalidDataException>(() => RcTfrag.Read(b));
        }

        var empty = new byte[0x10];
        I32(empty, 0, 0x10);
        Assert.Empty(RcTfrag.Read(empty).Indices);
    }

    [Fact]
    public void TfragDataPointersAndOffsetsAreClamped()
    {
        foreach (int data in HostileOffsets.Concat(new[] { 0, 0x40, 0x7ff0 }))
        {
            var b = new byte[0x10 + 0x40];
            I32(b, 0, 0x10);
            I32(b, 4, 1);
            I32(b, 0x10 + 0x10, data);
            U16(b, 0x10 + 0x16, 0xffff);
            U16(b, 0x10 + 0x18, 0xffff);
            U16(b, 0x10 + 0x1a, 0xffff);
            U16(b, 0x10 + 0x1e, 0xffff);
            b[0x10 + 0x20] = 0xff; b[0x10 + 0x21] = 0xff; b[0x10 + 0x22] = 0xff; b[0x10 + 0x29] = 0xff;
            Assert.Empty(RcTfrag.Read(b).Indices);
        }
    }

    [Fact]
    public void TfragFuzzOnlyFailsWithInvalidData()
    {
        FuzzOnlyInvalidData("tfrag-fuzz", 31, 3000, 0x10, 0x400, b => RcTfrag.Read(b));
        // Table pinned at 0x10 with a few tfrags: gets past the header gates far more often.
        var random = new Random(32);
        for (int iteration = 0; iteration < 1500; iteration++)
        {
            var b = new byte[random.Next(0x50, 0x300)];
            for (int at = 0x10; at < b.Length; at++)
            {
                b[at] = (byte)(random.Next(4) == 0 ? random.Next(256) : random.Next(8));
            }

            I32(b, 0, 0x10);
            I32(b, 4, random.Next(0, 4));
            var ex = Record.Exception(() => RcTfrag.Read(b));
            Assert.True(ex is null or InvalidDataException, $"tfrag iteration {iteration}: {ex?.GetType().Name}: {ex?.Message}");
        }
    }

    // --------------------------------------------------------- textures ----

    private static (byte[] Index, byte[] Assets, byte[] GsRam) TextureFixture(int dataOffset = 0, short width = 2, short height = 2, short slot = 0)
    {
        var index = new byte[16];
        I32(index, 0, dataOffset);
        BinaryPrimitives.WriteInt16LittleEndian(index.AsSpan(4), width);
        BinaryPrimitives.WriteInt16LittleEndian(index.AsSpan(6), height);
        BinaryPrimitives.WriteInt16LittleEndian(index.AsSpan(10), slot);
        return (index, new byte[4], new byte[1024]);
    }

    [Fact]
    public void TextureTableDecodesAValidEntry()
    {
        var (index, assets, gs) = TextureFixture();
        var texture = Assert.Single(RcLevelTextureTable.Read(index, assets, gs, 0, new(1, 0)));
        Assert.Equal(16, texture.Rgba.Length);
    }

    [Fact]
    public void TextureTableRejectsHostileRangesStrictlyAndSkipsThemPermissively()
    {
        var permissive = new RcLevelTextureTable.Options(Strict: false);
        var (index, assets, gs) = TextureFixture();
        foreach (var range in new RcLevelTextureTable.Range[] { new(-1, 0), new(1, -1), new(1, 1), new(2, 0), new(int.MaxValue, 0), new(1, int.MaxValue), new(int.MaxValue, int.MaxValue) })
        {
            Assert.Throws<InvalidDataException>(() => RcLevelTextureTable.Read(index, assets, gs, 0, range));
            Assert.Throws<InvalidDataException>(() => RcLevelTextureTable.Read(index, assets, gs, 0, range, permissive));
        }

        Assert.Throws<InvalidDataException>(() => RcLevelTextureTable.Read(index, assets, gs, -1, new(1, 0)));
        Assert.Throws<InvalidDataException>(() => RcLevelTextureTable.Read(index, assets, gs, assets.Length + 1, new(1, 0)));
        Assert.Empty(RcLevelTextureTable.Read([], [], [], 0, new(0, 0)));

        var bad = new[]
        {
            TextureFixture(width: 0), TextureFixture(height: 0), TextureFixture(width: -1), TextureFixture(width: 1025),
            TextureFixture(dataOffset: -1), TextureFixture(dataOffset: int.MaxValue), TextureFixture(dataOffset: 1),
            TextureFixture(slot: -1), TextureFixture(slot: 1), TextureFixture(slot: short.MaxValue),
            TextureFixture(width: short.MaxValue, height: short.MaxValue),
        };
        foreach (var (i, a, g) in bad)
        {
            Assert.Throws<InvalidDataException>(() => RcLevelTextureTable.Read(i, a, g, 0, new(1, 0)));
            Assert.Empty(RcLevelTextureTable.Read(i, a, g, 0, new(1, 0), permissive));
        }
    }

    [Fact]
    public void Ps2TextureDecodeRejectsBadDimensionsAndShortPixelBuffers()
    {
        var palette = new uint[256];
        foreach (var (w, h) in new[] { (0, 1), (1, 0), (-1, 1), (1, -1), (4097, 1), (1, 4097), (int.MaxValue, int.MaxValue) })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Ps2Texture.DecodePaletted8(new Ps2Texture.Paletted8(w, h, new byte[4], palette)));
        }

        Assert.Throws<InvalidDataException>(() => Ps2Texture.DecodePaletted8(new Ps2Texture.Paletted8(2, 2, new byte[3], palette)));
        Assert.Throws<InvalidDataException>(() => Ps2Texture.DecodePaletted8(new Ps2Texture.Paletted8(1, 1, ReadOnlyMemory<byte>.Empty, palette)));

        // A short palette is zero-extended; every 8-bit index stays addressable.
        var decoded = Ps2Texture.DecodePaletted8(new Ps2Texture.Paletted8(2, 1, new byte[] { 0, 255 }, [0xffffffffu]));
        Assert.Equal(8, decoded.Rgba.Length);
        Assert.Equal(0, Ps2Texture.DecodePaletted8(new Ps2Texture.Paletted8(1, 1, new byte[] { 200 }, [])).Rgba[3]);
        Assert.All(Enumerable.Range(0, 256), i => Assert.InRange(Ps2Texture.MapPaletteIndex(i), 0, 255));
    }

    // --------------------------------------------------------- skeleton ----

    private static byte[] ValidSkeleton()
    {
        var b = new byte[0xa5];
        b[8] = 1;
        I32(b, 0x14, 0x48);
        I32(b, 0x18, 0x88);
        I32(b, 0x1c, 0x98);
        F32(b, 0x24, 1024f);
        U16(b, 0x88 + 12, 0);
        I32(b, 0x98, 1);
        I32(b, 0x9c, 0xa0);
        U16(b, 0xa0, 1);
        b[0xa2] = 0;
        b[0xa4] = 0;
        return b;
    }

    [Fact]
    public void SkeletonDecodesAMinimalRig()
    {
        var source = Ps2Skeleton.Read(ValidSkeleton());
        Assert.Equal(-1, Assert.Single(source.Joints).Parent);
        Assert.Equal(0, Assert.Single(source.Chains).Target);
    }

    [Fact]
    public void SkeletonRejectsInconsistentTablesAsInvalidData()
    {
        void Case(Action<byte[]> edit)
        {
            byte[] b = ValidSkeleton();
            edit(b);
            Assert.Throws<InvalidDataException>(() => Ps2Skeleton.Read(b));
        }

        Case(b => b[8] = 0);                              // no joints
        Case(b => b[8] = 255);                            // tables run past the source
        Case(b => F32(b, 0x24, float.NaN));
        Case(b => F32(b, 0x24, 0));
        Case(b => F32(b, 0x24, -1));
        Case(b => F32(b, 0x14 + 0x34, float.PositiveInfinity));
        Case(b => I32(b, 0x14, int.MaxValue));
        Case(b => I32(b, 0x14, -1));
        Case(b => I32(b, 0x18, int.MaxValue - 8));
        Case(b => I32(b, 0x1c, int.MaxValue));
        Case(b => I32(b, 0x1c, int.MaxValue - 3));
        Case(b => U16(b, 0x88 + 12, 1));                  // misaligned parent offset
        Case(b => U16(b, 0x88 + 12, 64));                 // parent index == joint count
        Case(b => I32(b, 0x98, 0));                       // no chains
        Case(b => I32(b, 0x98, 257));                     // too many chains
        Case(b => I32(b, 0x98, int.MaxValue));
        Case(b => I32(b, 0x9c, int.MaxValue));
        Case(b => I32(b, 0x9c, int.MaxValue - 2));
        Case(b => I32(b, 0x9c, -4));
        Case(b => U16(b, 0xa0, 0));                       // empty chain
        Case(b => U16(b, 0xa0, 129));                     // chain too long
        Case(b => U16(b, 0xa0, 0xffff));
        Case(b => b[0xa4] = 1);                           // ancestor >= joint count
        Case(b => b[0xa2] = 200);                         // layer list past the source
    }

    [Fact]
    public void SkeletonSweepsOnlyFailWithInvalidData()
    {
        SweepPrefixesAndBytes("skeleton", ValidSkeleton(), b => Ps2Skeleton.Read(b));
        FuzzOnlyInvalidData("skeleton-fuzz", 41, 3000, 0, 0x200, b => Ps2Skeleton.Read(b));
    }

    private static byte[] ValidJointMap()
    {
        var b = new byte[0x50];
        b[8] = 2;
        I32(b, 0x1c, 0x48);
        b[0x4c] = 0; b[0x4d] = 1; b[0x4e] = 0xff;
        return b;
    }

    [Fact]
    public void AnimationJointMapIsBoundedAndInjective()
    {
        Assert.Equal(new[] { 0, 1 }, Ps2Skeleton.ReadAnimationJointMap(ValidJointMap(), 111));
        Assert.Throws<InvalidDataException>(() => Ps2Skeleton.ReadAnimationJointMap(ValidJointMap(), 1));
        Assert.Throws<InvalidDataException>(() => Ps2Skeleton.ReadAnimationJointMap(ValidJointMap(), 0));
        Assert.Throws<InvalidDataException>(() => Ps2Skeleton.ReadAnimationJointMap([], 111));

        void Case(Action<byte[]> edit)
        {
            byte[] b = ValidJointMap();
            edit(b);
            Assert.Throws<InvalidDataException>(() => Ps2Skeleton.ReadAnimationJointMap(b, 111));
        }

        Case(b => b[8] = 0);
        Case(b => b[8] = 255);
        Case(b => b[0x4e] = 0);       // missing terminator
        Case(b => b[0x4d] = 0);       // repeated joint
        Case(b => b[0x4d] = 200);     // joint escapes the target skeleton
        Case(b => I32(b, 0x1c, int.MaxValue));
        Case(b => I32(b, 0x1c, int.MaxValue - 4));
        Case(b => I32(b, 0x1c, -1));
        for (int length = 0; length < 0x4f; length++)
        {
            var ex = Record.Exception(() => Ps2Skeleton.ReadAnimationJointMap(ValidJointMap()[..length], 111));
            Assert.True(ex is InvalidDataException, $"truncation {length}: {ex?.GetType().Name}");
        }
    }

    // -------------------------------------------------------- animation ----

    private static byte[] RegularSequence()
    {
        var b = new byte[0x38];
        b[0x10] = 1;
        U32(b, 0x1c, 0x20);
        U16(b, 0x20 + 8, 8);
        return b;
    }

    private static byte[] PackedSequence()
    {
        var b = new byte[0x38];
        b[0x10] = 1;
        U32(b, 0x1c, 0xf000_0030u);
        return b;
    }

    [Fact]
    public void RegularAndPackedSequencesDecode()
    {
        var regular = Ps2SkeletalAnimation.ReadSequence(RegularSequence(), 0, 1, 7, sequenceRelativeFrames: false);
        Assert.Equal(7, regular.Id);
        Assert.Single(Assert.Single(regular.Frames).Rotations);

        var packed = Ps2SkeletalAnimation.ReadSequence(PackedSequence(), 0, 1, 8, sequenceRelativeFrames: false);
        Assert.Equal(32767, Assert.Single(Assert.Single(packed.Frames).Rotations).W);
    }

    [Fact]
    public void SequenceArgumentsAndOffsetsAreRangeChecked()
    {
        byte[] regular = RegularSequence();
        foreach (int joints in new[] { 0, -1, 257, int.MaxValue })
        {
            Assert.Throws<InvalidDataException>(() => Ps2SkeletalAnimation.ReadSequence(regular, 0, joints, 0, false));
        }

        foreach (int offset in new[] { -1, 0x1d, 0x38, 0x1000, int.MaxValue, int.MaxValue - 0x1b, int.MinValue })
        {
            Assert.Throws<InvalidDataException>(() => Ps2SkeletalAnimation.ReadSequence(regular, offset, 1, 0, false));
            Assert.Throws<InvalidDataException>(() => Ps2SkeletalAnimation.ReadSequence(regular, offset, 1, 0, true));
        }

        // Frame-offset table claims more frames/sounds than the source holds.
        byte[] counts = RegularSequence();
        counts[0x10] = 255;
        Assert.Throws<InvalidDataException>(() => Ps2SkeletalAnimation.ReadSequence(counts, 0, 1, 0, false));
        counts[0x10] = 1;
        counts[0x12] = 255;
        Assert.Throws<InvalidDataException>(() => Ps2SkeletalAnimation.ReadSequence(counts, 0, 1, 0, false));
        counts[0x10] = 0;
        Assert.Throws<InvalidDataException>(() => Ps2SkeletalAnimation.ReadSequence(counts, 0, 1, 0, false));

        // Mixed or unknown encodings are explicit NotSupported, not misread.
        byte[] unknown = RegularSequence();
        U32(unknown, 0x1c, 0x1000_0020u);
        Assert.Throws<NotSupportedException>(() => Ps2SkeletalAnimation.ReadSequence(unknown, 0, 1, 0, false));
    }

    [Fact]
    public void PackedFrameWhoseWindowEndsInsideTheChannelCountsIsInvalidDataNotAnIndexError()
    {
        // Two packed frames; the second starts exactly where the first frame's bounded window ends,
        // 2 bytes before the descriptor's trailing scale/translation counts.
        var b = new byte[0x60];
        b[0x10] = 2;
        U32(b, 0x1c, 0xf000_0020u);
        U32(b, 0x20, 0xf000_002eu);
        Assert.Throws<InvalidDataException>(() => Ps2SkeletalAnimation.ReadSequence(b, 0, 1, 0, false));
    }

    [Fact]
    public void SequenceSweepsOnlyFailWithInvalidData()
    {
        SweepPrefixesAndBytes("regular", RegularSequence(), b => Ps2SkeletalAnimation.ReadSequence(b, 0, 1, 0, false), typeof(NotSupportedException));
        SweepPrefixesAndBytes("packed", PackedSequence(), b => Ps2SkeletalAnimation.ReadSequence(b, 0, 1, 0, false), typeof(NotSupportedException));
        FuzzSequences(51, 0x00000000u);
        FuzzSequences(52, 0xf0000000u);
    }

    private static void FuzzSequences(int seed, uint flagBits)
    {
        var random = new Random(seed);
        for (int iteration = 0; iteration < 3000; iteration++)
        {
            var b = new byte[random.Next(0x30, 0x180)];
            for (int at = 0; at < b.Length; at++)
            {
                b[at] = (byte)(random.Next(5) == 0 ? random.Next(256) : random.Next(6));
            }

            b[0x10] = (byte)random.Next(1, 4);
            b[0x12] = (byte)random.Next(0, 3);
            for (int f = 0; f < b[0x10]; f++)
            {
                U32(b, 0x1c + f * 4, flagBits | (uint)random.Next(0x1c, b.Length));
            }

            int joints = random.Next(1, 5);
            var ex = Record.Exception(() => Ps2SkeletalAnimation.ReadSequence(b, 0, joints, 0, false));
            Assert.True(ex is null or InvalidDataException or NotSupportedException,
                $"sequence fuzz seed {seed} iteration {iteration}: {ex?.GetType().Name}: {ex?.Message}");
        }
    }

    // ------------------------------------------------------------- moby ----

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(int.MaxValue - 8)]
    [InlineData(int.MinValue)]
    public void MobyClassTablesRejectOverflowingEntryOffsets(int offset)
    {
        Assert.Empty(GcUyaMoby.ReadClasses(new byte[64], new byte[64], [], new(2, offset)));
    }

    [Fact]
    public void MobyReadersTreatShortBuffersAsAbsentNotAsCrashes()
    {
        foreach (int length in new[] { 0, 1, 0x0c, 0x13, 0x47 })
        {
            var buffer = new byte[length];
            Assert.Empty(GcUyaMoby.ReadJoints(buffer, 3));
            Assert.Empty(GcUyaMoby.ReadSequences(buffer, 3));
            Assert.Throws<InvalidDataException>(() => GcUyaMoby.ReadClass(buffer));
        }

        Assert.Empty(GcUyaMoby.ReadJoints(new byte[0x100], 0));
        Assert.Empty(GcUyaMoby.ReadJoints(new byte[0x100], 257));
        Assert.Empty(GcUyaMoby.ReadSequences(new byte[0x100], 0));
        Assert.Empty(GcUyaMoby.ReadSequences(new byte[0x100], -1));
    }

    [Fact]
    public void MobySkeletonOffsetsCannotOverflowTheBoundsChecks()
    {
        foreach (int offset in HostileOffsets.Concat(new[] { 0xe1, 0xff }))
        {
            var b = new byte[0x100];
            I32(b, 0x14, offset);   // skeleton
            I32(b, 0x18, 0x10);     // common translations
            Assert.Empty(GcUyaMoby.ReadJoints(b, 2));

            var swapped = new byte[0x100];
            I32(swapped, 0x14, 0x10);
            I32(swapped, 0x18, offset);
            Assert.Empty(GcUyaMoby.ReadJoints(swapped, 2));
        }
    }

    [Fact]
    public void ValidSkeletonWithTwoJointsStillDecodes()
    {
        var b = new byte[0x100];
        I32(b, 0x14, 0x48);
        I32(b, 0x18, 0x88);
        Assert.Equal(2, GcUyaMoby.ReadJoints(b, 2).Count);
    }

    [Fact]
    public void MobySequenceOffsetsCannotOverflowTheBoundsChecks()
    {
        foreach (int offset in HostileOffsets.Concat(new[] { 0x100 - 0x1b, 0x100, 0x48 }))
        {
            var b = new byte[0x100];
            b[0x0c] = 1;
            I32(b, 0x48, offset);
            Assert.All(GcUyaMoby.ReadSequences(b, 2), sequence => Assert.Empty(sequence.Frames));
        }

        // The final legal 0x1c-byte sequence header must still be accepted.
        var boundary = new byte[0x100];
        boundary[0x0c] = 1;
        I32(boundary, 0x48, 0x100 - 0x1c);
        Assert.Empty(Assert.Single(GcUyaMoby.ReadSequences(boundary, 2)).Frames);

        // A sequence whose frame table runs past the buffer is kept without frames.
        var tooMany = new byte[0x100];
        tooMany[0x0c] = 1;
        I32(tooMany, 0x48, 0x60);
        tooMany[0x60 + 0x10] = 255;
        Assert.Empty(Assert.Single(GcUyaMoby.ReadSequences(tooMany, 2)).Frames);

        // Frame entries pointing outside the class are skipped, never dereferenced.
        foreach (uint entry in new[] { 0u, 0x0fff_ffffu, 0x0000_0100u, 0x0000_00f8u, 0xf000_0100u })
        {
            var b = new byte[0x100];
            b[0x0c] = 1;
            I32(b, 0x48, 0x60);
            b[0x60 + 0x10] = 1;
            U32(b, 0x60 + 0x1c, entry);
            Assert.Empty(Assert.Single(GcUyaMoby.ReadSequences(b, 2)).Frames);
        }
    }

    [Fact]
    public void MobyPacketTableOffsetsCannotOverflowTheBoundsChecks()
    {
        foreach (int table in HostileOffsets.Concat(new[] { 0x48, 0x100 - 0x0f, 0x100 }))
        {
            var b = new byte[0x100];
            I32(b, 0x00, table);
            b[0x04] = 2;            // high-LOD packet count
            F32(b, 0x24, 1024f);
            var mesh = GcUyaMoby.ReadClass(b);
            Assert.Empty(mesh.Indices);
        }
    }

    [Fact]
    public void MobyFuzzOnlyFailsWithInvalidData()
    {
        FuzzOnlyInvalidData("moby-fuzz", 61, 3000, 0x48, 0x400, b =>
        {
            GcUyaMoby.ReadClass(b);
            GcUyaMoby.ReadClass(b, recoverPersistentVertexCache: true);
            GcUyaMoby.ReadJoints(b, b[8]);
            GcUyaMoby.ReadSequences(b, b[8]);
        });
    }
}
