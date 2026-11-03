using System.Buffers.Binary;
using OBP.PS2.Animation;
using OBP.PS2.Collision;
using OBP.PS2.Geometry;

namespace OBP.Tests;

/// <summary>
/// Synthetic-only hostile-input regressions for the shared geometry, collision
/// and skeletal-animation readers. Every fixture is hand-built bytes; offsets
/// near <see cref="int.MaxValue"/> must not wrap past a bounds check.
/// </summary>
public sealed class MalformedInputGeometryTests
{
    private static void I32(byte[] b, int at, int v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at), v);
    private static void U32(byte[] b, int at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), v);
    private static void U16(byte[] b, int at, int v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), (ushort)v);

    // ---------------------------------------------------------------- collision

    [Fact]
    public void CollisionRejectsTruncatedAndBadHeaders()
    {
        Assert.Throws<InvalidDataException>(() => RcCollision.Read([]));
        Assert.Throws<InvalidDataException>(() => RcCollision.Read(new byte[7]));
        var badMesh = new byte[16];
        I32(badMesh, 0, 4);
        Assert.Throws<InvalidDataException>(() => RcCollision.Read(badMesh));
        I32(badMesh, 0, 1 << 30);
        Assert.Throws<InvalidDataException>(() => RcCollision.Read(badMesh));
        var badHero = new byte[16];
        I32(badHero, 0, 8);
        I32(badHero, 4, 1 << 30);
        Assert.Throws<InvalidDataException>(() => RcCollision.Read(badHero));
    }

    [Fact]
    public void CollisionEmptyOctreeIsAnEmptyMesh()
    {
        var bytes = new byte[16];
        I32(bytes, 0, 8);
        var mesh = RcCollision.Read(bytes);
        Assert.Empty(mesh.Octants);
        Assert.Empty(mesh.Triangles);
    }

    [Fact]
    public void CollisionNodeOffsetNearIntMaxDoesNotWrapPastTheBoundsCheck()
    {
        // mesh @8: zCoord, zCount=1, z offset 2 (=> node @16); y node has one entry
        // whose offset puts the x node at 0x7fffffff, where "+2" overflows.
        var bytes = new byte[32];
        I32(bytes, 0, 8);
        U16(bytes, 10, 1);
        U16(bytes, 12, 2);
        U16(bytes, 18, 1);
        U32(bytes, 20, 0x7FFF_FFF7);
        Assert.Throws<InvalidDataException>(() => RcCollision.Read(bytes));
    }

    [Fact]
    public async Task CollisionAliasedEmptyGridIsBoundedByTheTraversalBudget()
    {
        // Every z entry aliases one y node and every y entry aliases one all-empty x
        // node: 65535^3 empty cells would otherwise be walked from a ~640 KiB file.
        const int count = 65535;
        const int yNode = 131088;
        int xNode = yNode + 4 + count * 4;
        var bytes = new byte[xNode + 4 + count * 4];
        I32(bytes, 0, 8);
        U16(bytes, 10, count);
        for (int z = 0; z < count; z++)
        {
            U16(bytes, 12 + z * 2, (yNode - 8) / 4);
        }

        U16(bytes, yNode + 2, count);
        for (int y = 0; y < count; y++)
        {
            U32(bytes, yNode + 4 + y * 4, (uint)(xNode - 8));
        }

        U16(bytes, xNode + 2, count);

        var task = Task.Run(() => Record.Exception(() => RcCollision.Read(bytes)));
        var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(20)));
        Assert.True(finished == task, "RcCollision.Read did not finish on an aliased empty grid.");
        Assert.IsType<InvalidDataException>(await task);
    }

    // --------------------------------------------------------------------- tie

    [Fact]
    public void TieRejectsShortBufferAndIgnoresPacketOffsetsThatWrapPastTheBuffer()
    {
        Assert.Throws<InvalidDataException>(() => RcTie.ReadClass(new byte[0x6F], RcTie.Rac1Layout));

        var buf = new byte[0x100];
        I32(buf, 0, 0x70);          // packet table
        buf[RcTie.Rac1Layout.PacketCountOffset] = 1;
        I32(buf, 0x70, 0x7FFF_FF7F); // 0x70 + value = 0x7fffffef; +0x2c overflows int
        var mesh = RcTie.ReadClass(buf, RcTie.Rac1Layout);
        Assert.Empty(mesh.Indices);
    }

    [Fact]
    public void TieMaterialTableBeyondBufferIsRejected()
    {
        var buf = new byte[0x80];
        buf[RcTie.Rac2Layout.TextureCountOffset] = 200;
        I32(buf, RcTie.Rac2Layout.AdGifOffsetOffset, int.MaxValue);
        Assert.Throws<InvalidDataException>(() => RcTie.ReadClass(buf, RcTie.Rac2Layout));
    }

    // ------------------------------------------------------------------- shrub

    [Fact]
    public void ShrubRejectsShortBufferAndSkipsPacketsThatWrapPastTheBuffer()
    {
        Assert.Throws<InvalidDataException>(() => RcShrub.ReadClass(new byte[0x3F]));

        var buf = new byte[0x48];
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(0x28), 1);
        I32(buf, 0x40, int.MaxValue);
        I32(buf, 0x44, 2);
        Assert.Empty(RcShrub.ReadClass(buf).Indices);
    }

    [Fact]
    public void ShrubSkipsPacketsWhoseHeaderUnpackIsShorterThanTheHeader()
    {
        // Three tiny UNPACK V2-8 packets: the first "header" carries 4 data bytes, not 12+.
        var buf = new byte[0x48 + 24];
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(0x28), 1);
        I32(buf, 0x40, 0x48);
        I32(buf, 0x44, 24);
        for (int i = 0; i < 3; i++)
        {
            U32(buf, 0x48 + i * 8, 0x6601_0000u);
        }

        Assert.Empty(RcShrub.ReadClass(buf).Indices);
    }

    // --------------------------------------------------------------------- sky

    [Fact]
    public void SkyRejectsShortBufferAndImplausibleShellCounts()
    {
        Assert.Throws<InvalidDataException>(() => RcSky.Read(new byte[0x3F]));
        var many = new byte[0x40];
        BinaryPrimitives.WriteInt16LittleEndian(many.AsSpan(0x06), 9);
        Assert.Throws<InvalidDataException>(() => RcSky.Read(many));
    }

    [Fact]
    public void SkyShellOffsetNearIntMaxIsIgnoredNotWrapped()
    {
        var bytes = new byte[0x40];
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0x06), 1);
        I32(bytes, 0x20, 0x7FFF_FFF8);
        Assert.Empty(RcSky.Read(bytes).Shells);
    }

    [Fact]
    public void SkyClusterDataOffsetsThatWrapAreSkipped()
    {
        var bytes = new byte[0x80];
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0x06), 1);
        I32(bytes, 0x20, 0x40);
        I32(bytes, 0x40, 1);            // one cluster
        int cluster = 0x50;
        I32(bytes, cluster + 0x10, 0x7FFF_FFF0);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(cluster + 0x14), 1);
        U16(bytes, cluster + 0x18, 0x20);
        U16(bytes, cluster + 0x1a, 0x20);
        U16(bytes, cluster + 0x1c, 0x20);
        Assert.Empty(RcSky.Read(bytes).Shells);
    }

    [Fact]
    public void SkyTextureOffsetsThatWrapAreSkipped()
    {
        var bytes = new byte[0x80];
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0x0c), 1);
        I32(bytes, 0x10, 0x40);          // texture definitions
        I32(bytes, 0x14, 0x7FFF_F000);   // data base
        I32(bytes, 0x40 + 0x00, 0x1000); // palette offset: dataBase + 0x1000 wraps
        I32(bytes, 0x40 + 0x04, 0);
        I32(bytes, 0x40 + 0x08, 4);
        I32(bytes, 0x40 + 0x0c, 4);
        Assert.Empty(RcSky.Read(bytes).Textures);
    }

    // ------------------------------------------------------------ tfrag / tie

    [Fact]
    public void TfragRejectsTruncatedBlobsAndTablesBeyondTheBlob()
    {
        Assert.Throws<InvalidDataException>(() => RcTfrag.Read(new byte[0x0F]));
        var bad = new byte[0x20];
        I32(bad, 0, 0x10);
        I32(bad, 4, int.MaxValue);
        Assert.Throws<InvalidDataException>(() => RcTfrag.Read(bad));
        I32(bad, 4, -1);
        Assert.Throws<InvalidDataException>(() => RcTfrag.Read(bad));
    }

    [Fact]
    public void TfragDataOffsetNearIntMaxIsSkippedNotWrapped()
    {
        var blob = new byte[0x10 + 0x40];
        I32(blob, 0, 0x10);
        I32(blob, 4, 1);
        I32(blob, 0x10 + 0x10, int.MaxValue);
        Assert.Empty(RcTfrag.Read(blob).Indices);
    }

    // ------------------------------------------------------ skeletal animation

    private static byte[] TwoFramePackedSequence()
    {
        // Header 0x1c, two frame offsets, no sound cues. Frame 0 is bounded by frame 1's
        // offset, which sits exactly where the joint-descriptor's channel counts start.
        var b = new byte[0x40];
        b[0x10] = 2; // frames
        U32(b, 0x1c, 0xF000_0028u);
        U32(b, 0x20, 0xF000_002Eu);
        return b;
    }

    [Fact]
    public void AnimationRejectsEmptyAndTruncatedSequencesWithTypedErrors()
    {
        Assert.Throws<InvalidDataException>(() => Ps2SkeletalAnimation.ReadSequence([], 0, 1, 0, false));
        Assert.Throws<InvalidDataException>(() => Ps2SkeletalAnimation.ReadSequence(new byte[0x1c], 0, 1, 0, false));
        Assert.Throws<InvalidDataException>(() => Ps2SkeletalAnimation.ReadSequence(new byte[0x40], 0, 0, 0, false));
        Assert.Throws<InvalidDataException>(() => Ps2SkeletalAnimation.ReadSequence(new byte[0x40], 0, 257, 0, false));
        Assert.Throws<InvalidDataException>(() => Ps2SkeletalAnimation.ReadSequence(new byte[0x40], int.MaxValue - 4, 1, 0, false));
        Assert.Throws<InvalidDataException>(() => Ps2SkeletalAnimation.ReadSequence(new byte[0x40], -1, 1, 0, false));

        var frameBeyondSource = new byte[0x30];
        frameBeyondSource[0x10] = 1;
        U32(frameBeyondSource, 0x1c, 0x0FFF_FFFFu);
        Assert.Throws<InvalidDataException>(() => Ps2SkeletalAnimation.ReadSequence(frameBeyondSource, 0, 1, 0, false));
    }

    [Fact]
    public void AnimationPackedChannelCountsOutsideTheBoundedFrameAreATypedError() =>
        Assert.Throws<InvalidDataException>(() =>
            Ps2SkeletalAnimation.ReadSequence(TwoFramePackedSequence(), 0, joints: 1, id: 0, sequenceRelativeFrames: false));

    // -------------------------------------------------------------------- moby

    [Fact]
    public void MobySequenceAndJointReadersTolerateShortBuffersAndWrappingOffsets()
    {
        Assert.Empty(GcUyaMoby.ReadSequences(new byte[4], 1));
        Assert.Empty(GcUyaMoby.ReadJoints(new byte[4], 1));

        var buf = new byte[0x60];
        buf[0x0c] = 1;
        I32(buf, 0x48, int.MaxValue);
        Assert.Empty(GcUyaMoby.ReadSequences(buf, 1));
    }
}
