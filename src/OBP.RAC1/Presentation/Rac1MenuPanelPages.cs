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

    /// <summary>
    /// Original single-item text-list screens paired with the ordinary
    /// pause page's first seven class-0x472 panel slots. Slot 6 has a
    /// separate native menu_system.unkD8 admission gate and must not be
    /// awakened as an always-visible ordinary choice.
    /// </summary>
    public sealed record RootEntry(int Slot, int TextId, int Action,
        int Flags, uint DrawCallback, bool RequiresNativeAdmission);

    private readonly IReadOnlyDictionary<Page, Assignment> _pages;
    private Rac1MenuPanelPages(IReadOnlyDictionary<Page, Assignment> pages,
        IReadOnlyList<RootEntry> rootEntries)
    {
        _pages = pages;
        NativeRootEntries = rootEntries;
    }

    public IReadOnlyDictionary<Page, Assignment> Pages => _pages;
    public Assignment this[Page page] => _pages[page];
    public IReadOnlyList<RootEntry> NativeRootEntries { get; }

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
        var mapped = new List<(uint Address, int Offset, int Size, uint Kind)>();
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
            mapped.Add((address, cursor + 16, checked((int)length), kind));
            cursor = checked(cursor + 16 + (int)length);
        }

        var pages = new Dictionary<Page, Assignment>();
        foreach (var (page, address) in NativePageAddresses)
        {
            const int HeaderBytes = (PanelSlots + 2) * 4;
            var section = mapped.SingleOrDefault(s => s.Kind == 1 && address >= s.Address &&
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
        // Ordinary text screens are authored as pointer-valued MenuPage
        // screens[14] after the 0x44 header. Decode only initialized
        // overlay data; BSS or guessed neighboring pages are not authority.
        uint ReadSourceWord(uint address)
        {
            var section = mapped.SingleOrDefault(s => s.Kind == 1 &&
                address >= s.Address && (ulong)address + 4 <= (ulong)s.Address + (uint)s.Size);
            if (section.Size == 0)
                throw new InvalidDataException($"R&C1 pause text address 0x{address:x} is not initialized retail data.");
            return BinaryPrimitives.ReadUInt32LittleEndian(
                overlay.AsSpan(section.Offset + checked((int)(address - section.Address)), 4));
        }

        const uint rootPage = 0x001B2588;
        var entries = new List<RootEntry>(7);
        for (int slot = 0; slot < PanelSlots; slot++)
        {
            uint screen = ReadSourceWord(rootPage + 0x44u + (uint)(slot * 4));
            if (screen == 0)
            {
                if (slot < 7)
                    throw new InvalidDataException("Original R&C1 pause root is missing an authored text screen.");
                continue;
            }
            if (slot >= 7)
                throw new InvalidDataException("Original R&C1 pause root has unexpected additional text screens.");

            uint draw = ReadSourceWord(screen + 4);
            uint flags = ReadSourceWord(screen + 0x30);
            uint items = ReadSourceWord(screen + 0x34);
            uint first = ReadSourceWord(items);
            int textId = (ushort)first;
            int action = (ushort)(first >> 16);
            if (ReadSourceWord(screen) != 0x00278840 || draw != 0x00278E10 ||
                flags != 4 || textId == 0 || action != 3 ||
                (ReadSourceWord(items + 8) & 0xFFFF) != 0 ||
                (ReadSourceWord(items + 12) & 0xFFFF) != 0)
                throw new InvalidDataException($"R&C1 root text screen {slot} differs from the supported native callback/list shape.");
            entries.Add(new RootEntry(slot, textId, action, checked((int)flags),
                draw, RequiresNativeAdmission: slot == 6));
        }
        return new Rac1MenuPanelPages(pages, entries);
    }
}
