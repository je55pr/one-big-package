using System.Buffers.Binary;
using OBP.IO;
using OBP.RAC1.Level;

namespace OBP.RAC1.Presentation;

/// <summary>
/// Page-to-class-0x472 animation assignments, read from the original Veldin
/// level overlay. R&C1's MenuPage begins with fourteen int32 animation IDs.
/// The page addresses are source-backed from FUN_L00_00276368, not inferred
/// from animation artwork. No retail code or artwork is persisted.
/// </summary>
public sealed class Rac1MenuPanelPages
{
    public const int PanelSlots = 14;
    private const int MaxOverlayBytes = 8 * 1024 * 1024;

    public enum Page
    {
        OrdinaryPause,
        Opening,
        Startup,
        Save,
        Context,
        Progress
    }

    private static readonly (Page Page, int NativeAddress)[] NativePageAddresses =
    [
        (Page.OrdinaryPause, 0x001B2588),
        (Page.Opening, 0x001B86C8),
        (Page.Startup, 0x001B3518),
        (Page.Save, 0x001B6088),
        (Page.Context, 0x001B6B38),
        (Page.Progress, 0x001B71F0),
    ];

    public sealed record Assignment(Page Page, int NativeAddress, IReadOnlyList<int> Animations, int State)
    {
        public int ForSlot(int slot) => (uint)slot < PanelSlots
            ? Animations[slot] : throw new ArgumentOutOfRangeException(nameof(slot));
    }

    private readonly IReadOnlyDictionary<Page, Assignment> _pages;
    private Rac1MenuPanelPages(IReadOnlyDictionary<Page, Assignment> pages) => _pages = pages;
    public IReadOnlyDictionary<Page, Assignment> Pages => _pages;
    public Assignment this[Page page] => _pages[page];

    public static Rac1MenuPanelPages Read(IRandomAccessReader disc, Rac1DiscIndex.NativeLevel level0)
    {
        if (level0.LevelId != 0)
            throw new ArgumentException("Menu page native addresses are pinned to the Veldin overlay.", nameof(level0));
        var levelData = Rac1DiscIndex.OpenRange(disc, level0, 0);
        var directory = Rac1LevelData.ReadDirectory(levelData);
        var overlayReader = Rac1LevelData.OpenRange(levelData, directory, 0);
        if (overlayReader.Length is < 32 or > MaxOverlayBytes)
            throw new InvalidDataException("R&C1 menu overlay exceeds its supported bounds.");
        byte[] overlay = overlayReader.Read(0, checked((int)overlayReader.Length));
        // Retail level overlay is a sequence of 16-byte (loadAddress,
        // payloadLength, kind, entry) headers, with direct mapped 1=data
        // payloads and 8=BSS sections. Only initialized source data may
        // provide menu animation IDs.
        var mapped = new List<(uint Address, int Offset, int Size)>();
        int cursor = 0;
        while (cursor < overlay.Length)
        {
            if (overlay.Length - cursor < 16)
                throw new InvalidDataException("Truncated R&C1 level overlay section header.");
            uint address = BinaryPrimitives.ReadUInt32LittleEndian(overlay.AsSpan(cursor));
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(overlay.AsSpan(cursor + 4));
            uint kind = BinaryPrimitives.ReadUInt32LittleEndian(overlay.AsSpan(cursor + 8));
            if ((address & 3) != 0 || length == 0 || length > overlay.Length - cursor - 16 ||
                kind is not (1 or 8))
                throw new InvalidDataException("Invalid R&C1 menu overlay section header.");
            mapped.Add((address, cursor + 16, checked((int)length)));
            cursor = checked(cursor + 16 + (int)length);
        }

        var pages = new Dictionary<Page, Assignment>();
        foreach (var (page, address) in NativePageAddresses)
        {
            const int HeaderBytes = (PanelSlots + 2) * 4;
            var section = mapped.SingleOrDefault(s => address >= s.Address &&
                (long)address + HeaderBytes <= (long)s.Address + s.Size);
            if (section.Size == 0)
                throw new InvalidDataException($"R&C1 source menu page 0x{address:x} not mapped.");
            int at = section.Offset + checked((int)(address - section.Address));
            var anims = new int[PanelSlots];
            for (int slot = 0; slot < PanelSlots; slot++)
            {
                int anim = BinaryPrimitives.ReadInt32LittleEndian(overlay.AsSpan(at + slot * 4));
                if (anim is < 0 or >= 216)
                    throw new InvalidDataException($"Menu page 0x{address:x} slot {slot} references invalid 0x472 animation {anim}.");
                anims[slot] = anim;
            }
            int state = BinaryPrimitives.ReadInt32LittleEndian(overlay.AsSpan(at + 0x3C));
            pages.Add(page, new Assignment(page, address, anims, state));
        }
        return new Rac1MenuPanelPages(pages);
    }
}
