using System.Buffers.Binary;
using OBP.IO;

namespace OBP.Tests.Helpers;

/// <summary>
/// Minimal synthetic PS2 ISO-9660 image with a SYSTEM.CNF and a tiny ELF32 MIPS
/// boot executable. Ported 1:1 from
/// <c>reference-ts/tests/helpers/synthetic-ps2.mjs</c> so the C# ISO / ELF /
/// boot readers are exercised against the same bytes as the TypeScript tests.
/// </summary>
public static class SyntheticPs2
{
    public const int Block = 2048;

    public sealed record Fixture(byte[] Bytes, int ConfigLength, int ExecutableLength);

    public sealed record Options(
        string? BootPath = null,
        int ElfMachine = 8,
        int ElfType = 2,
        bool IncludeBootExecutable = true);

    public static Fixture Build(string serialExecutable, Options? options = null)
    {
        options ??= new Options();
        string bootPath = options.BootPath ?? $"cdrom0:\\{serialExecutable};1";
        string configText = $"BOOT2 = {bootPath}\r\nVER = 1.00\r\nVMODE = NTSC\r\n";
        byte[] configBytes = configText.Select(c => (byte)c).ToArray();
        byte[] executableBytes = BuildElf(options.ElfMachine, options.ElfType);

        var bytes = new byte[24 * Block];
        int pvd = 16 * Block;
        bytes[pvd] = 1;
        WriteAscii(bytes, pvd + 1, 5, "CD001");
        bytes[pvd + 6] = 1;
        WriteAscii(bytes, pvd + 40, 32, "RATCHET TEST");
        WriteBoth32(bytes, pvd + 80, 24);
        WriteBoth16(bytes, pvd + 128, Block);
        WriteDirectoryRecord(bytes, pvd + 156, identifierByte: 0, extentLba: 20, dataLength: Block, directory: true);

        int terminator = 17 * Block;
        bytes[terminator] = 255;
        WriteAscii(bytes, terminator + 1, 5, "CD001");
        bytes[terminator + 6] = 1;

        int root = 20 * Block;
        root += WriteDirectoryRecord(bytes, root, identifierByte: 0, extentLba: 20, dataLength: Block, directory: true);
        root += WriteDirectoryRecord(bytes, root, identifierByte: 1, extentLba: 20, dataLength: Block, directory: true);
        root += WriteDirectoryRecord(bytes, root, identifier: "SYSTEM.CNF;1", extentLba: 21, dataLength: configBytes.Length);
        if (options.IncludeBootExecutable)
        {
            root += WriteDirectoryRecord(bytes, root, identifier: $"{serialExecutable};1", extentLba: 22, dataLength: executableBytes.Length);
            executableBytes.CopyTo(bytes, 22 * Block);
        }

        configBytes.CopyTo(bytes, 21 * Block);
        return new Fixture(bytes, configBytes.Length, executableBytes.Length);
    }

    public static IRandomAccessReader Reader(string serialExecutable, Options? options = null) =>
        new InMemoryReader("synthetic.iso", Build(serialExecutable, options).Bytes);

    private static byte[] BuildElf(int machine, int type)
    {
        var b = new byte[256];
        new byte[] { 0x7f, 0x45, 0x4c, 0x46, 1, 1, 1, 0, 0 }.CopyTo(b, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(16), (ushort)type);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(18), (ushort)machine);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(24), 0x00100008);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(28), 52);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(32), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(36), 0x20924001);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(40), 52);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(42), 32);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(44), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(46), 40);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(48), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(50), 0);
        // one program header at file offset 52
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(52 + 0), 1);          // PT_LOAD
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(52 + 4), 128);        // offset
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(52 + 8), 0x00100000); // vaddr
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(52 + 12), 0x00100000);// paddr
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(52 + 16), 16);        // filesz
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(52 + 20), 32);        // memsz
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(52 + 24), 5);         // flags
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(52 + 28), 16);        // align
        for (int i = 0; i < 16; i++)
        {
            b[128 + i] = (byte)((i * 13 + 7) & 0xff);
        }

        return b;
    }

    private static void WriteBoth16(byte[] bytes, int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset + 2), value);
    }

    private static void WriteBoth32(byte[] bytes, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset + 4), value);
    }

    private static void WriteAscii(byte[] bytes, int offset, int length, string value)
    {
        for (int i = 0; i < length; i++)
        {
            bytes[offset + i] = (byte)(i < value.Length ? value[i] : ' ');
        }
    }

    private static int WriteDirectoryRecord(byte[] bytes, int offset, int extentLba, int dataLength, bool directory = false, string? identifier = null, int? identifierByte = null)
    {
        byte[] id = identifier is not null
            ? identifier.Select(c => (byte)c).ToArray()
            : new[] { (byte)identifierByte! };
        int length = 33 + id.Length + (id.Length % 2 == 0 ? 1 : 0);
        bytes[offset] = (byte)length;
        bytes[offset + 1] = 0;
        WriteBoth32(bytes, offset + 2, (uint)extentLba);
        WriteBoth32(bytes, offset + 10, (uint)dataLength);
        bytes[offset + 25] = (byte)(directory ? 0x02 : 0);
        WriteBoth16(bytes, offset + 28, 1);
        bytes[offset + 32] = (byte)id.Length;
        id.CopyTo(bytes, offset + 33);
        return length;
    }
}

/// <summary>In-memory <see cref="IRandomAccessReader"/> for tests.</summary>
public sealed class InMemoryReader(string name, byte[] bytes) : IRandomAccessReader
{
    public string Name => name;
    public long Length => bytes.Length;

    public void Read(long offset, Span<byte> destination)
    {
        RandomAccessReaderExtensions.ValidateRange(Name, Length, offset, destination.Length);
        bytes.AsSpan((int)offset, destination.Length).CopyTo(destination);
    }
}
