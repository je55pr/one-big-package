using System.Buffers.Binary;
using System.Globalization;
using OBP.RAC3.Level;

namespace OBP.Tests;

/// <summary>
/// Characterization of the strict UYA gameplay reader on small synthetic
/// buffers (no retail bytes). Unlike the Going Commando reader this one throws
/// <see cref="InvalidDataException"/> with a specific message on any malformed
/// block, so both the accepted layout and the rejection text are pinned.
/// </summary>
public sealed class Rac3GameplayParseTests
{
    private const int BufferSize = 0x1000;
    private const int TiePtr = 0x34, ShrubPtr = 0x40, MobyPtr = 0x4c, PvarTablePtr = 0x5c, PvarDataPtr = 0x60;
    private const int TargetVolumePtr = 0x68, TargetPolygonPtr = 0x78, TargetGroupPtr = 0x98;
    private const int TieBlock = 0x200, ShrubBlock = 0x300, MobyBlock = 0x400, PvarTable = 0x600, PvarData = 0x680;
    private const int VolumeBlock = 0x700, PolygonBlock = 0x800, GroupOuter = 0x900;

    private static void I32(byte[] b, int at, int v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at), v);
    private static void F32(byte[] b, int at, float v) => BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(at), v);
    private static void S16(byte[] b, int at, short v) => BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(at), v);

    private static byte[] Populated(bool withTargets = true)
    {
        var d = new byte[BufferSize];
        I32(d, TiePtr, TieBlock); I32(d, TieBlock, 1);
        I32(d, TieBlock + 0x10, 41);
        for (int m = 0; m < 16; m++) F32(d, TieBlock + 0x20 + m * 4, m + 0.5f);

        I32(d, ShrubPtr, ShrubBlock); I32(d, ShrubBlock, 0);

        I32(d, MobyPtr, MobyBlock); I32(d, MobyBlock, 1);
        int moby = MobyBlock + 0x10;
        I32(d, moby, 0x88);
        I32(d, moby + 0x10, 901); I32(d, moby + 0x14, 902);
        I32(d, moby + 0x28, 5821); F32(d, moby + 0x2c, 2f);
        F32(d, moby + 0x40, 1); F32(d, moby + 0x44, 2); F32(d, moby + 0x48, 3);
        F32(d, moby + 0x4c, 0.25f); F32(d, moby + 0x50, 0.5f); F32(d, moby + 0x54, 0.75f);
        I32(d, moby + 0x68, 0);
        I32(d, moby + 0x70, 0x1234);

        I32(d, PvarTablePtr, PvarTable); I32(d, PvarDataPtr, PvarData);
        I32(d, PvarTable, 2); I32(d, PvarTable + 4, 2);
        d[PvarData + 2] = 0x7e; d[PvarData + 3] = 0x7f;

        if (!withTargets) return d;

        // One oriented volume.
        I32(d, TargetVolumePtr, VolumeBlock); I32(d, VolumeBlock, 1);
        int vol = VolumeBlock + 0x10;
        F32(d, vol + 0x30, 10); F32(d, vol + 0x34, 20); F32(d, vol + 0x38, 30);
        F32(d, vol + 0x40, 1); F32(d, vol + 0x54, 1); F32(d, vol + 0x68, 1);

        // One triangle (0,0) (10,0) (0,10): offset table, then a 0x40-byte record.
        I32(d, TargetPolygonPtr, PolygonBlock);
        I32(d, PolygonBlock, 1); I32(d, PolygonBlock + 4, 0x20); I32(d, PolygonBlock + 8, 0x40);
        I32(d, PolygonBlock + 0x10, 0);
        int poly = PolygonBlock + 0x20;
        I32(d, poly, 3);
        F32(d, poly + 0x10 + 0x10, 10); F32(d, poly + 0x20 + 0x14, 10);

        // One group (radius 50 around the origin) referencing polygon 0 and volume 0.
        I32(d, TargetGroupPtr, GroupOuter);
        I32(d, GroupOuter, 0x60);
        int payload = GroupOuter + 4;
        I32(d, payload, 1);
        I32(d, payload + 4, 0x50); I32(d, payload + 8, 0x54);
        for (int list = 2; list < 5; list++) I32(d, payload + 4 + list * 4, 0x58);
        int group = payload + 0x20;
        F32(d, group + 0x0c, 50);
        S16(d, group + 0x10, 1); S16(d, group + 0x12, 1);
        I32(d, payload + 0x50, 0); I32(d, payload + 0x54, 0);
        return d;
    }

    [Fact]
    public void ParsesMatricesMobiesAndPvarPayloads()
    {
        byte[] data = Populated(withTargets: false);
        Rac3Gameplay.Gameplay g = Rac3Gameplay.Parse(data);

        Assert.Same(data, g.RawDecoded);
        var tie = Assert.Single(g.TieInstances);
        Assert.Equal((0, 41), (tie.Index, tie.OClass));
        Assert.Equal(Enumerable.Range(0, 16).Select(i => i + 0.5f), tie.Matrix);
        Assert.Empty(g.ShrubInstances);

        var moby = Assert.Single(g.MobyInstances);
        Assert.Equal((0, 5821, 2f), (moby.Index, moby.OClass, moby.Scale));
        Assert.Equal((1f, 2f, 3f), moby.Position);
        Assert.Equal((0.25f, 0.5f, 0.75f), moby.Rotation);
        Assert.Equal((901, 902, 0, 0x1234), (moby.UidCompatibility, moby.Raw0x14, moby.PvarIndex, moby.ModeBits));
        Assert.Equal(0x88, moby.RawInstance.Length);
        Assert.Equal([0x7e, 0x7f], moby.PvarData);

        Assert.Empty(g.TargetVolumes);
        Assert.Empty(g.TargetPolygons);
        Assert.Empty(g.TargetGroups);
    }

    [Fact]
    public void NegativePvarIndexYieldsNoPayload()
    {
        byte[] data = Populated(withTargets: false);
        I32(data, MobyBlock + 0x10 + 0x68, -1);

        Assert.Null(Assert.Single(Rac3Gameplay.Parse(data).MobyInstances).PvarData);
    }

    [Fact]
    public void ParsesTargetVolumePolygonAndGroupTables()
    {
        Rac3Gameplay.Gameplay g = Rac3Gameplay.Parse(Populated());

        var volume = Assert.Single(g.TargetVolumes);
        Assert.Equal((10f, 20f, 30f), volume.Center);
        Assert.Equal((1f, 0f, 0f), volume.InverseColumnX);
        Assert.Equal((0f, 1f, 0f), volume.InverseColumnY);
        Assert.Equal((0f, 0f, 1f), volume.InverseColumnZ);
        Assert.Equal(0x80, volume.RawRecord.Length);
        Assert.True(volume.Contains(10f, 20f, 30f));
        Assert.True(volume.Contains(11f, 20f, 30f)); // inclusive face
        Assert.False(volume.Contains(11.01f, 20f, 30f));

        var polygon = Assert.Single(g.TargetPolygons);
        Assert.Equal(3, polygon.Vertices.Count);
        Assert.Equal(new Rac3Gameplay.TargetPolygonVertex(10, 0, 0, 0), polygon.Vertices[1]);
        Assert.Equal(new Rac3Gameplay.TargetPolygonVertex(0, 10, 0, 0), polygon.Vertices[2]);
        Assert.Equal(0x40, polygon.RawRecord.Length);
        Assert.True(polygon.Contains(1f, 1f));
        Assert.False(polygon.Contains(20f, 1f));

        var group = Assert.Single(g.TargetGroups);
        Assert.Equal((0f, 0f, 0f), group.Center);
        Assert.Equal(50f, group.Radius);
        Assert.Equal([0], group.PolygonRegionIndices);
        Assert.Equal([0], group.OrientedVolumeIndices);
        Assert.Empty(group.UnknownList2);
        Assert.Empty(group.UnknownList3);
        Assert.Empty(group.UnknownList4);

        Assert.True(g.TryContainsTargetGroup(0, 1f, 1f, 0f, out bool inside));
        Assert.True(inside);
        Assert.True(g.TryContainsTargetGroup(0, 100f, 0f, 0f, out inside));
        Assert.False(inside);
        Assert.False(g.TryContainsTargetGroup(1, 0f, 0f, 0f, out inside));
        Assert.False(inside);
    }

    [Fact]
    public void AbsentOptionalTargetPointersAreEmpty()
    {
        byte[] data = Populated();
        I32(data, TargetVolumePtr, 0); I32(data, TargetPolygonPtr, 0); I32(data, TargetGroupPtr, 0);

        Rac3Gameplay.Gameplay g = Rac3Gameplay.Parse(data);

        Assert.Empty(g.TargetVolumes);
        Assert.Empty(g.TargetPolygons);
        Assert.Empty(g.TargetGroups);
    }

    public static TheoryData<string, Action<byte[]>, string> Rejections() => new()
    {
        { "tie pointer zero", d => I32(d, TiePtr, 0), "UYA TIE instances pointer 0x0 lies outside gameplay data." },
        { "pvar table past end", d => I32(d, PvarTablePtr, BufferSize), $"UYA PVar table pointer 0x{BufferSize:x} lies outside gameplay data." },
        { "tie count", d => I32(d, TieBlock, 200_001), "UYA TIE instances count/extent is invalid (200001)." },
        { "tie extent", d => I32(d, TieBlock, 100_000), "UYA TIE instances count/extent is invalid (100000)." },
        { "non-finite matrix", d => F32(d, TieBlock + 0x20, float.NaN), "UYA TIE instances 0 contains a non-finite matrix component." },
        { "moby size", d => I32(d, MobyBlock + 0x10, 0x80), "UYA Moby 0 size is 0x80, expected 0x88." },
        { "moby non-finite", d => F32(d, MobyBlock + 0x10 + 0x44, float.PositiveInfinity), "UYA Moby 0 contains a non-finite float at +0x44." },
        { "moby pvar index outside table", d => I32(d, MobyBlock + 0x10 + 0x68, 1_000_000), "UYA PVar index 1000000 lies outside the table." },
        { "pvar range", d => I32(d, PvarTable + 4, 100_000), "UYA PVar 0 range 2+100000 lies outside gameplay data." },
        { "volume non-finite", d => F32(d, VolumeBlock + 0x10 + 0x34, float.NaN), "UYA target volume 0 has a non-finite float at +0x34." },
        { "volume count", d => I32(d, VolumeBlock, 100_000), "UYA target-volume count/extent is invalid (100000)." },
        { "polygon offset outside blob", d => I32(d, PolygonBlock + 0x10, 0x40), "UYA target polygon 0 offset 0x40 lies outside its data blob." },
        { "polygon vertices overflow record", d => I32(d, PolygonBlock + 0x20, 4), "UYA target polygon 0 vertex count/extent is invalid (4)." },
        { "polygon vertex non-finite", d => F32(d, PolygonBlock + 0x20 + 0x10, float.NaN), "UYA target polygon 0 vertex 0 has a non-finite component." },
        { "group payload size", d => I32(d, GroupOuter, 0x10), "UYA target-group payload size 0x10 is invalid." },
        { "group negative radius", d => F32(d, GroupOuter + 4 + 0x20 + 0x0c, -1f), "UYA target group 0 has negative radius -1." },
        { "group polygon index", d => I32(d, GroupOuter + 4 + 0x50, 5), "UYA target group 0 references an out-of-range target polygon." },
        { "group volume index", d => I32(d, GroupOuter + 4 + 0x54, 5), "UYA target group 0 references an out-of-range target volume." },
        { "group list base", d => I32(d, GroupOuter + 4 + 4, 0x7000), "UYA target-group list 0 base 0x7000 lies outside its payload." },
        { "tie header truncated", d => I32(d, TiePtr, BufferSize - 8), "UYA TIE instances header is truncated." },
        { "shrub header truncated", d => I32(d, ShrubPtr, BufferSize - 8), "UYA shrub instances header is truncated." },
        { "moby header truncated", d => I32(d, MobyPtr, BufferSize - 8), "UYA Moby block header is truncated." },
        { "volume header truncated", d => I32(d, TargetVolumePtr, BufferSize - 8), "UYA target-volume header is truncated." },
        { "polygon header truncated", d => I32(d, TargetPolygonPtr, BufferSize - 8), "UYA target-polygon header is truncated." },
        { "group outer header truncated", d => I32(d, TargetGroupPtr, BufferSize - 2), "UYA target-group outer header is truncated." },
        { "negative moby count", d => I32(d, MobyBlock, -1), "UYA Moby count/extent is invalid (-1)." },
        { "negative tie count", d => I32(d, TieBlock, -1), "UYA TIE instances count/extent is invalid (-1)." },
        { "negative shrub count", d => I32(d, ShrubBlock, -1), "UYA shrub instances count/extent is invalid (-1)." },
        { "negative group count", d => I32(d, GroupOuter + 4, -1), "UYA target-group count/record extent is invalid (-1)." },
        { "negative optional pointer", d => I32(d, TargetVolumePtr, -4), "UYA target volume pointer 0xfffffffc lies outside gameplay data." },
        { "polygon table overlaps data", d => I32(d, PolygonBlock + 4, 0), "UYA target-polygon count/extent is invalid (1, rel=0x0, bytes=0x40)." },
        { "polygon large offset", d => I32(d, PolygonBlock + 0x10, int.MaxValue), "UYA target polygon 0 offset 0x7fffffff lies outside its data blob." },
        { "polygon nonmonotonic", d => { I32(d, PolygonBlock, 2); I32(d, PolygonBlock + 0x10, 0x20); I32(d, PolygonBlock + 0x14, 0x10); }, "UYA target polygon offsets are not monotonic at 1." },
        { "polygon header overlaps next", d => I32(d, PolygonBlock, 2), "UYA target polygon 0 header overlaps the next record." },
        { "negative group list count", d => S16(d, GroupOuter + 4 + 0x20 + 0x10, -1), "UYA target group 0 list 0 has negative count -1." },
        { "group list range", d => I32(d, GroupOuter + 4 + 0x20 + 0x1c, 0x7000), "UYA target group 0 list 0 range 0x7000+1 lies outside its payload." },
        { "group nonfinite centre", d => F32(d, GroupOuter + 4 + 0x20, float.NaN), "UYA target group 0 has a non-finite float at +0x0." },
        { "two malformed blocks use Moby-first validation", d => { I32(d, MobyBlock, -1); I32(d, TieBlock, -1); }, "UYA Moby count/extent is invalid (-1)." },
    };

    [Theory]
    [MemberData(nameof(Rejections))]
    public void MalformedBlocksAreRejectedWithTheirNamedReason(string name, Action<byte[]> corrupt, string message)
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            // The parser's diagnostic numbers use the current culture; pin the
            // message-contract test to one locale and restore the caller's setting.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            byte[] data = Populated();
            corrupt(data);

            var error = Assert.Throws<InvalidDataException>(() => Rac3Gameplay.Parse(data));
            Assert.True(message == error.Message, $"{name}: expected \"{message}\" but got \"{error.Message}\"");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void BuffersShorterThanTheHeaderAreRejected()
    {
        var error = Assert.Throws<InvalidDataException>(() => Rac3Gameplay.Parse(new byte[0x67]));
        Assert.Equal("UYA gameplay is only 103 bytes.", error.Message);
    }
}
