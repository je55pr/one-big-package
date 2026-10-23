using System.Buffers.Binary;
using System.Text;
using OBP.RAC1.Presentation;

namespace OBP.Tests;

public sealed class Rac1RetailPanelBackingTests
{
    [Fact]
    public void NativePanelBackingIsOpaqueEightEightSixteen()
    {
        uint packed = Rac1RetailPanelBlend.NativePanelClearRgba;
        Assert.Equal(0x80100808u, packed);
        Assert.Equal(8, (byte)packed);
        Assert.Equal(8, (byte)(packed >> 8));
        Assert.Equal(16, (byte)(packed >> 16));
        Assert.Equal(128, (byte)(packed >> 24));
        Assert.Equal(128, Rac1RetailPanelBlend.ClearAlpha);
    }

    [Fact]
    public void PackedBackingAgreesWithOriginalExecutableMappedAddress()
    {
        string? path = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        using var stream = File.OpenRead(path);
        byte[] pvd = ReadAt(stream, 16L * 2048, 2048);
        Assert.Equal("CD001", Encoding.ASCII.GetString(pvd, 1, 5));
        const int rootRecord = 156;
        int dirLba = checked((int)ReadU32(pvd, rootRecord + 2));
        int dirLength = checked((int)ReadU32(pvd, rootRecord + 10));
        Assert.InRange(dirLength, 34, 2048 * 8);
        byte[] root = ReadAt(stream, (long)dirLba * 2048, dirLength);
        int exeLba = -1;
        int exeLength = 0;
        for (int at = 0; at < root.Length;)
        {
            int recSize = root[at];
            if (recSize == 0)
            {
                at = (at / 2048 + 1) * 2048;
                continue;
            }
            Assert.True(recSize >= 34 && at + recSize <= root.Length);
            int nameLen = root[at + 32];
            string name = Encoding.ASCII.GetString(root, at + 33, nameLen);
            if (name == "SCUS_971.99;1")
            {
                exeLba = checked((int)ReadU32(root, at + 2));
                exeLength = checked((int)ReadU32(root, at + 10));
                break;
            }
            at += recSize;
        }
        Assert.True(exeLba > 0 && exeLength > 0, "NTSC-U R&C1 executable not found in ISO9660 root");
        long elfStart = (long)exeLba * 2048;
        byte[] elf = ReadAt(stream, elfStart, 52);
        Assert.Equal([0x7f, (byte)'E', (byte)'L', (byte)'F'], elf.Take(4).ToArray());
        int phOffset = checked((int)ReadU32(elf, 28));
        int phSize = BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(42));
        int phCount = BinaryPrimitives.ReadUInt16LittleEndian(elf.AsSpan(44));
        Assert.InRange(phCount, 1, 32);
        const uint nativeAddress = 0x001601B0;
        bool found = false;
        for (int i = 0; i < phCount; i++)
        {
            byte[] ph = ReadAt(stream, elfStart + phOffset + i * phSize, phSize);
            if (ReadU32(ph, 0) != 1) continue; // PT_LOAD
            uint offset = ReadU32(ph, 4), vaddr = ReadU32(ph, 8), filesz = ReadU32(ph, 16);
            if (nativeAddress < vaddr || (ulong)nativeAddress + 4 > (ulong)vaddr + filesz) continue;
            long mapped = elfStart + offset + nativeAddress - vaddr;
            Assert.InRange(mapped - elfStart, 0, exeLength - 4);
            byte[] original = ReadAt(stream, mapped, 4);
            Assert.Equal(Rac1RetailPanelBlend.NativePanelClearRgba, ReadU32(original, 0));
            found = true;
            break;
        }
        Assert.True(found, "ELF program headers did not map panel_clear_color at 0x001601B0");
    }

    private static uint ReadU32(byte[] value, int at) =>
        BinaryPrimitives.ReadUInt32LittleEndian(value.AsSpan(at, 4));

    private static byte[] ReadAt(Stream stream, long address, int size)
    {
        stream.Position = address;
        byte[] bytes = new byte[size];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
