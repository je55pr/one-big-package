using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Audio;

namespace OBP.RAC2.Audio;

public readonly record struct GcAudioRange(int Sector, int SizeBytes)
{
    public long OffsetBytes => (long)Sector * 2048;
    public bool Present => Sector > 0 && SizeBytes > 0;
}

/// <summary>Going Commando per-level AUDIO<n>.WAD catalogue.</summary>
public sealed class GcLevelAudioWad
{
    public const int HeaderSize = 0x1018;
    public const int BinCount = 511;

    public int SectorWord { get; }
    public IReadOnlyList<GcAudioRange> Bins { get; }
    public GcAudioRange UpgradeSample { get; }
    public GcAudioRange ThermanatorFreeze { get; }
    public GcAudioRange ThermanatorThaw { get; }

    private readonly IRandomAccessReader _reader;

    private GcLevelAudioWad(
        IRandomAccessReader reader,
        int sectorWord,
        IReadOnlyList<GcAudioRange> bins,
        GcAudioRange upgradeSample,
        GcAudioRange freeze,
        GcAudioRange thaw)
    {
        _reader = reader;
        SectorWord = sectorWord;
        Bins = bins;
        UpgradeSample = upgradeSample;
        ThermanatorFreeze = freeze;
        ThermanatorThaw = thaw;
    }

    public static GcLevelAudioWad Open(IRandomAccessReader reader)
    {
        if (reader.Length < HeaderSize)
        {
            throw new InvalidDataException($"GC level audio WAD '{reader.Name}' is shorter than the 0x{HeaderSize:x} header.");
        }

        byte[] h = reader.Read(0, HeaderSize);
        int declared = BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(0, 4));
        if (declared != HeaderSize)
        {
            throw new InvalidDataException($"GC level audio WAD header size is 0x{declared:x}, expected 0x{HeaderSize:x}.");
        }

        var bins = new GcAudioRange[BinCount];
        for (int i = 0; i < bins.Length; i++)
        {
            bins[i] = ReadRange(h, 0x08 + i * 8, reader, $"bin[{i}]");
        }

        return new GcLevelAudioWad(
            reader,
            BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(4, 4)),
            bins,
            ReadRange(h, 0x1000, reader, "upgrade"),
            ReadRange(h, 0x1008, reader, "thermanator-freeze"),
            ReadRange(h, 0x1010, reader, "thermanator-thaw"));
    }

    public IRandomAccessReader OpenBin(int index)
    {
        if ((uint)index >= Bins.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return OpenRange(Bins[index], $"bin[{index}]");
    }

    public Vagp.Stream OpenVag(int index) => Vagp.Open(OpenBin(index));

    /// <summary>
    /// Resolve the native environment <c>music_track</c> selector to its paired
    /// mono left/right VAG streams. GC selectors advance in units of four:
    /// selector 0 -> bins 0/1, selector 4 -> bins 2/3.
    /// </summary>
    public (Vagp.Stream Left, Vagp.Stream Right) ResolveMusicPair(short musicTrack)
    {
        if (musicTrack < 0 || (musicTrack & 3) != 0)
        {
            throw new InvalidDataException($"Unsupported GC music_track selector {musicTrack}; expected a non-negative multiple of four.");
        }

        int left = (musicTrack / 4) * 2;
        if (left + 1 >= Bins.Count || !Bins[left].Present || !Bins[left + 1].Present)
        {
            throw new InvalidDataException($"GC music_track selector {musicTrack} has no populated stereo VAG pair.");
        }

        return (OpenVag(left), OpenVag(left + 1));
    }

    private IRandomAccessReader OpenRange(GcAudioRange range, string label)
    {
        if (!range.Present)
        {
            throw new InvalidDataException($"GC level audio {label} is absent.");
        }

        return new SubRangeReader(_reader, range.OffsetBytes, range.SizeBytes, $"{_reader.Name}#{label}");
    }

    private static GcAudioRange ReadRange(byte[] h, int at, IRandomAccessReader reader, string label)
    {
        int sector = BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(at, 4));
        int size = BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(at + 4, 4));
        var range = new GcAudioRange(sector, size);
        if ((sector == 0) != (size == 0) || sector < 0 || size < 0)
        {
            throw new InvalidDataException($"GC level audio {label} has invalid range sector={sector}, size={size}.");
        }

        if (range.Present && (range.OffsetBytes > reader.Length || size > reader.Length - range.OffsetBytes))
        {
            throw new InvalidDataException($"GC level audio {label} range lies outside '{reader.Name}'.");
        }

        return range;
    }
}

/// <summary>Going Commando global AUDIO.WAD sector catalogue.</summary>
public sealed class GcGlobalAudioWad
{
    public const int HeaderSize = 0x1800;

    public enum Group
    {
        Vendor,
        HelpEnglish,
        HelpFrench,
        HelpGerman,
        HelpSpanish,
        HelpItalian,
    }

    public sealed record Entry(Group Group, int Index, int Sector, int ExtentSizeBytes);

    public IReadOnlyList<Entry> Entries { get; }
    private readonly IRandomAccessReader _reader;

    private GcGlobalAudioWad(IRandomAccessReader reader, IReadOnlyList<Entry> entries)
    {
        _reader = reader;
        Entries = entries;
    }

    public static GcGlobalAudioWad Open(IRandomAccessReader reader)
    {
        if (reader.Length < HeaderSize)
        {
            throw new InvalidDataException($"GC global audio WAD '{reader.Name}' is shorter than the 0x{HeaderSize:x} header.");
        }

        byte[] h = reader.Read(0, HeaderSize);
        int declared = BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(0, 4));
        if (declared != HeaderSize)
        {
            throw new InvalidDataException($"GC global audio WAD header size is 0x{declared:x}, expected 0x{HeaderSize:x}.");
        }

        var starts = new List<(Group Group, int Index, int Sector)>();
        Add(Group.Vendor, 0x0008, 254);
        Add(Group.HelpEnglish, 0x0400, 256);
        Add(Group.HelpFrench, 0x0800, 256);
        Add(Group.HelpGerman, 0x0c00, 256);
        Add(Group.HelpSpanish, 0x1000, 256);
        Add(Group.HelpItalian, 0x1400, 256);

        starts.Sort((a, b) => a.Sector.CompareTo(b.Sector));
        var entries = new List<Entry>(starts.Count);
        for (int i = 0; i < starts.Count; i++)
        {
            long start = (long)starts[i].Sector * 2048;
            long end = i + 1 < starts.Count ? (long)starts[i + 1].Sector * 2048 : reader.Length;
            if (start < HeaderSize || start >= reader.Length || end <= start || end > reader.Length)
            {
                throw new InvalidDataException($"GC global audio entry {starts[i].Group}[{starts[i].Index}] has an invalid sector extent.");
            }

            entries.Add(new Entry(starts[i].Group, starts[i].Index, starts[i].Sector, checked((int)(end - start))));
        }

        return new GcGlobalAudioWad(reader, entries);

        void Add(Group group, int at, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int sector = BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(at + i * 4, 4));
                if (sector < 0)
                {
                    throw new InvalidDataException($"GC global audio {group}[{i}] has negative sector {sector}.");
                }

                if (sector != 0)
                {
                    starts.Add((group, i, sector));
                }
            }
        }
    }

    public Vagp.Stream OpenVag(Group group, int index)
    {
        Entry entry = Entries.FirstOrDefault(e => e.Group == group && e.Index == index)
            ?? throw new KeyNotFoundException($"GC global audio entry {group}[{index}] is absent.");
        return Vagp.Open(new SubRangeReader(
            _reader,
            (long)entry.Sector * 2048,
            entry.ExtentSizeBytes,
            $"{_reader.Name}#{group}[{index}]"));
    }
}
