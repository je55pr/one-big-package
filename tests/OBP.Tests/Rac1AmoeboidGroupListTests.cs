using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1AmoeboidGroupListTests
{
    [Fact]
    public void PackedNativeMembershipRetainsTraversalOrderAndTerminalIndex()
    {
        // Native lists do not sort. High bit on the final halfword marks
        // termination while the remaining 15 bits retain the final index.
        byte[] words = [90, 0, 5, 0, 14, 0x80, 99, 0];
        Assert.Equal(new[] { 90, 5, 14 }, Rac1AmoeboidGroupList.Decode(words));
        Assert.Empty(Rac1AmoeboidGroupList.Decode([]));
        Assert.Throws<InvalidDataException>(() => Rac1AmoeboidGroupList.Decode([1]));
        Assert.Throws<InvalidDataException>(() => Rac1AmoeboidGroupList.Decode([90, 0, 14, 0]));
    }

    [SkippableFact]
    public void RetailSplitHelperReadsPackedMobyGroupHalfwordsAndTerminalBit()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_RAC1_ISO unavailable");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(x => x.LevelId == 1);
        var overlay = Rac1ExecutableOverlay.Read(reader, level);
        static uint Word(Rac1ExecutableOverlay o, int a) =>
            BinaryPrimitives.ReadUInt32LittleEndian(o.Read(a, 4));

        // Source helper FUN_L01_002ef350 indexes a table at 0x1abcc0
        // by Moby.group (+0x21) then traverses 16-bit index words. Its
        // 0x7fff mask, 0x100-byte Moby stride and high-bit terminal
        // control flow prove the representation, not the loader order.
        Assert.Equal(0x91430021u, Word(overlay, 0x002ef384));
        Assert.Equal(0x3c02001bu, Word(overlay, 0x002ef388));
        Assert.Equal(0x2442bcc0u, Word(overlay, 0x002ef38c));
        Assert.Equal(0x8c650000u, Word(overlay, 0x002ef398));
        Assert.Equal(0x94a20000u, Word(overlay, 0x002ef3c0));
        Assert.Equal(0x30427fffu, Word(overlay, 0x002ef3cc));
        Assert.Equal(0x00021200u, Word(overlay, 0x002ef3d0));
        Assert.Equal(0x00061400u, Word(overlay, 0x002ef3e0));
        Assert.Equal(0x0440fffau, Word(overlay, 0x002ef3b8));
        Assert.Equal(0x24a50002u, Word(overlay, 0x002ef3bc));
    }
}
