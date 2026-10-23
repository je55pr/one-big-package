using System.Buffers.Binary;
using System.Security.Cryptography;
using OBP.IO;

namespace OBP.RAC1.Level;

/// <summary>Bounded virtual-address reads of the original level's sectioned executable.</summary>
public sealed class Rac1ExecutableOverlay
{
    private const int EeBytes = 32 * 1024 * 1024;
    private readonly byte[] _payload;
    private readonly List<(int Address, int Offset, int Size)> _sections = [];

    private Rac1ExecutableOverlay(byte[] payload)
    {
        _payload = payload;
        int offset = 0;
        int? entry = null;
        while (offset < payload.Length)
        {
            if (payload.Length - offset < 16) throw new InvalidDataException("Truncated R&C1 overlay section header.");
            int I(int relative) => BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + relative));
            int address = I(0), size = I(4), kind = I(8), currentEntry = I(12);
            if (address < 0 || address % 4 != 0 || size < 0 || size % 4 != 0 ||
                address > EeBytes - size || size > payload.Length - offset - 16 || kind is not (1 or 8))
                throw new InvalidDataException("R&C1 executable overlay section exceeds its source or EE memory.");
            if (entry is not null && entry != currentEntry ||
                _sections.Any(s => address < s.Address + s.Size && s.Address < address + size))
                throw new InvalidDataException("R&C1 executable overlay has inconsistent entry points or overlapping sections.");
            entry = currentEntry;
            _sections.Add((address, offset + 16, size));
            offset += 16 + size;
        }
        EntryPoint = entry ?? throw new InvalidDataException("Empty R&C1 executable overlay.");
        _ = Read(EntryPoint, 4);
    }

    public int EntryPoint { get; }
    public string Sha256 => Convert.ToHexString(SHA256.HashData(_payload)).ToLowerInvariant();

    public static Rac1ExecutableOverlay Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > EeBytes) throw new InvalidDataException("R&C1 executable overlay exceeds EE memory.");
        return new(payload.ToArray());
    }

    public static Rac1ExecutableOverlay Read(IRandomAccessReader disc, Rac1DiscIndex.NativeLevel level)
    {
        var data = Rac1DiscIndex.OpenRange(disc, level, 0);
        var range = Rac1LevelData.OpenRange(data, Rac1LevelData.ReadDirectory(data), 0);
        if (range.Length > EeBytes) throw new InvalidDataException("R&C1 executable overlay exceeds EE memory.");
        return Parse(range.Read(0, checked((int)range.Length)));
    }

    public ReadOnlySpan<byte> Read(int address, int size)
    {
        if (address < 0 || size < 0 || address > EeBytes - size)
            throw new InvalidDataException("R&C1 overlay address exceeds EE memory.");
        foreach (var section in _sections)
            if (address >= section.Address && address - section.Address <= section.Size - size)
                return _payload.AsSpan(section.Offset + address - section.Address, size);
        throw new InvalidDataException($"R&C1 virtual range 0x{address:x}+{size} is not in one executable section.");
    }

    /// <summary>Research replay adapter; unmapped memory remains owned by the caller.</summary>
    public void CopyLoadedSections(Span<byte> eeMemory)
    {
        if (eeMemory.Length != EeBytes) throw new ArgumentException("Expected a complete EE address space.", nameof(eeMemory));
        foreach (var section in _sections)
            _payload.AsSpan(section.Offset, section.Size).CopyTo(eeMemory.Slice(section.Address, section.Size));
    }
}
