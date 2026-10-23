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

    public sealed record WeaponCell(int NativeId, ushort Icon, short Frame, short Kind);
    public sealed record WeaponsGrid(int PageAddress, int ScreenAddress, int Rows, int Columns,
        int Flags, float MarginX, float MarginY, IReadOnlyList<WeaponCell> Cells);
    public sealed record GadgetGroup(int PanelSlot, int ScreenAddress, int Rows, int Columns,
        int Flags, int Selected, float MarginX, float MarginY, int UpScreen, int DownScreen,
        IReadOnlyList<WeaponCell> Cells);
    public sealed record GadgetsGrid(int PageAddress, int InitialFocusScreen,
        IReadOnlyList<GadgetGroup> Groups);
    public sealed record QuickSelectGroup(int PanelSlot, int ScreenAddress, int Rows,
        int Columns, int Flags, float MarginX, float MarginY,
        int UpScreen, int DownScreen, IReadOnlyList<WeaponCell> Cells);
    public sealed record QuickSelectPage(int PageAddress, int InitialFocusScreen,
        IReadOnlyList<QuickSelectGroup> Groups);

    private readonly IReadOnlyDictionary<Page, Assignment> _pages;
    private Rac1MenuPanelPages(IReadOnlyDictionary<Page, Assignment> pages,
        IReadOnlyList<RootEntry> rootEntries, WeaponsGrid weaponsGrid,
        GadgetsGrid gadgetsGrid, QuickSelectPage quickSelectPage)
    {
        _pages = pages;
        NativeRootEntries = rootEntries;
        NativeWeaponsGrid = weaponsGrid;
        NativeGadgetsGrid = gadgetsGrid;
        NativeQuickSelectPage = quickSelectPage;
    }

    public IReadOnlyDictionary<Page, Assignment> Pages => _pages;
    public Assignment this[Page page] => _pages[page];
    public IReadOnlyList<RootEntry> NativeRootEntries { get; }
    public WeaponsGrid NativeWeaponsGrid { get; }
    public GadgetsGrid NativeGadgetsGrid { get; }
    public QuickSelectPage NativeQuickSelectPage { get; }

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
        // Root text action 3 points to the native Weapons MenuPage. Its
        // focused third screen is a 5-by-3 grid, with fifteen authored
        // ten-byte MenuGridCell records. Preserve the order and icons rather
        // than relying on the host inventory enum's incidental sorting.
        uint firstRootScreen = ReadSourceWord(rootPage + 0x44u);
        uint firstItemList = ReadSourceWord(firstRootScreen + 0x34u);
        uint weaponsPage = ReadSourceWord(firstItemList + 4u);
        uint weaponsScreen = ReadSourceWord(weaponsPage + 0x44u + 2u * 4u);
        int rows = checked((int)ReadSourceWord(weaponsScreen + 0x40u));
        int columns = checked((int)ReadSourceWord(weaponsScreen + 0x44u));
        int gridFlags = checked((int)ReadSourceWord(weaponsScreen + 0x30u));
        float marginX = BitConverter.Int32BitsToSingle((int)ReadSourceWord(weaponsScreen + 0x34u));
        float marginY = BitConverter.Int32BitsToSingle((int)ReadSourceWord(weaponsScreen + 0x38u));
        if (weaponsPage != 0x001B2DB8 ||
            ReadSourceWord(weaponsPage + 0x40u) != weaponsScreen ||
            ReadSourceWord(weaponsPage + 0x38u) != rootPage ||
            ReadSourceWord(weaponsScreen) != 0x002794A0 ||
            ReadSourceWord(weaponsScreen + 4) != 0x0027B590 ||
            rows != 3 || columns != 5 || gridFlags != 0x20 ||
            !float.IsFinite(marginX) || !float.IsFinite(marginY) ||
            marginX is < 0 or >= 1 || marginY is < 0 or >= 1)
            throw new InvalidDataException("R&C1 Weapons page does not contain the expected native five-column grid.");
        uint cellsPointer = ReadSourceWord(weaponsScreen + 0x48u);
        var cells = new List<WeaponCell>(checked(rows * columns));
        for (int cell = 0; cell < rows * columns; cell++)
        {
            uint at = cellsPointer + (uint)(cell * 10);
            uint first = ReadSourceWord(at);
            uint second = ReadSourceWord(at + 4);
            int nativeId = (short)(second >> 16);
            short kind = unchecked((short)second);
            short frame = unchecked((short)(first >> 16));
            ushort icon = unchecked((ushort)first);
            if (nativeId is < 1 or > 63 || kind != 0 || frame != 0 || icon is < 0xEA60 or > 0xEAC9 ||
                cells.Any(existing => existing.NativeId == nativeId))
                throw new InvalidDataException($"R&C1 Weapons grid cell {cell} is not a supported retail weapon.");
            cells.Add(new WeaponCell(nativeId, icon, frame, kind));
        }
        // The root Gadgets choice links to four different MenuItemGridData
        // screens, not one 5-column list. They form an authored vertical
        // navigation ring. Keep the per-group flags because native
        // menu_system.unk134/unk138 can skip conditional sections.
        uint gadgetRootScreen = ReadSourceWord(rootPage + 0x44u + 4u);
        uint gadgetItemList = ReadSourceWord(gadgetRootScreen + 0x34u);
        uint gadgetsPage = ReadSourceWord(gadgetItemList + 4u);
        uint gadgetFocus = ReadSourceWord(gadgetsPage + 0x40u);
        if (gadgetsPage != 0x001B28E8 || ReadSourceWord(gadgetsPage + 0x38u) != rootPage)
            throw new InvalidDataException("R&C1 Gadgets page linkage differs from the retail overlay.");
        int[] gadgetSlots = [2, 3, 9, 10];
        int[] expectedRows = [2, 1, 1, 1];
        int[] expectedColumns = [3, 3, 3, 2];
        int[] expectedFlags = [0x20, 0x4, 0x8, 0x10];
        var gadgetGroups = new List<GadgetGroup>(gadgetSlots.Length);
        for (int groupIndex = 0; groupIndex < gadgetSlots.Length; groupIndex++)
        {
            int slot = gadgetSlots[groupIndex];
            uint screen = ReadSourceWord(gadgetsPage + 0x44u + (uint)(slot * 4));
            int r = checked((int)ReadSourceWord(screen + 0x40u));
            int c = checked((int)ReadSourceWord(screen + 0x44u));
            int f = checked((int)ReadSourceWord(screen + 0x30u));
            int selected = checked((int)ReadSourceWord(screen + 0x3cu));
            float mx = BitConverter.Int32BitsToSingle((int)ReadSourceWord(screen + 0x34u));
            float my = BitConverter.Int32BitsToSingle((int)ReadSourceWord(screen + 0x38u));
            if (ReadSourceWord(screen) != 0x002794A0 || ReadSourceWord(screen + 4) != 0x0027B590 ||
                r != expectedRows[groupIndex] || c != expectedColumns[groupIndex] ||
                f != expectedFlags[groupIndex] || selected < 0 || selected >= r * c ||
                !float.IsFinite(mx) || !float.IsFinite(my) ||
                mx is < 0 or >= 1 || my is < 0 or >= 1)
                throw new InvalidDataException($"R&C1 Gadgets grid screen slot {slot} is not the retail shape.");
            uint sourceCells = ReadSourceWord(screen + 0x48u);
            var groupCells = new List<WeaponCell>(r * c);
            for (int index = 0; index < r * c; index++)
            {
                uint at = sourceCells + (uint)(index * 10);
                uint first = ReadSourceWord(at);
                uint second = ReadSourceWord(at + 4);
                int id = (short)(second >> 16);
                ushort icon = unchecked((ushort)first);
                short frame = unchecked((short)(first >> 16));
                short kind = unchecked((short)second);
                if (id is < 1 or > 36 || icon is < 0xEA60 or > 0xEAC9 || frame != 0 ||
                    kind != 0 || groupCells.Any(cell => cell.NativeId == id) ||
                    gadgetGroups.Any(g => g.Cells.Any(cell => cell.NativeId == id)))
                    throw new InvalidDataException($"R&C1 Gadgets grid cell {slot}:{index} is invalid.");
                groupCells.Add(new WeaponCell(id, icon, frame, kind));
            }
            gadgetGroups.Add(new GadgetGroup(slot, checked((int)screen), r, c, f,
                selected, mx, my,
                checked((int)ReadSourceWord(screen + 0x4cu)),
                checked((int)ReadSourceWord(screen + 0x50u)), groupCells));
        }
        if (gadgetFocus != (uint)gadgetGroups[1].ScreenAddress)
            throw new InvalidDataException("R&C1 Gadgets initial focused grid differs from source.");
        for (int i = 0; i < gadgetGroups.Count; i++)
        {
            var group = gadgetGroups[i];
            int expectedUp = gadgetGroups[(i + gadgetGroups.Count - 1) % gadgetGroups.Count].ScreenAddress;
            int expectedDown = gadgetGroups[(i + 1) % gadgetGroups.Count].ScreenAddress;
            if (group.UpScreen != expectedUp || group.DownScreen != expectedDown)
                throw new InvalidDataException("R&C1 Gadgets linked grid navigation is not a four-group ring.");
        }
        // Quick Select uses the original Weapons 15-cell array and a
        // second SIX-GADGET candidate grid. This second 2x3 is NOT the
        // set of quick-select slots: the native save has eight slot entries
        // and update_menu_item_slots manages those independently.
        uint quickRootScreen = ReadSourceWord(rootPage + 0x44u + 8u);
        uint quickPage = ReadSourceWord(ReadSourceWord(quickRootScreen + 0x34u) + 4u);
        uint quickFocus = ReadSourceWord(quickPage + 0x40u);
        if (quickPage != 0x001B32B8 || ReadSourceWord(quickPage + 0x38u) != rootPage)
            throw new InvalidDataException("R&C1 Quick Select link differs from the authored overlay.");
        int[] quickSlots = [2, 3];
        int[] quickRows = [3, 2];
        int[] quickColumns = [5, 3];
        int[] quickFlags = [0x20, 0];
        var quickGroups = new List<QuickSelectGroup>(2);
        for (int groupIndex = 0; groupIndex < quickSlots.Length; groupIndex++)
        {
            int slot = quickSlots[groupIndex];
            uint screen = ReadSourceWord(quickPage + 0x44u + (uint)(slot * 4));
            int r = checked((int)ReadSourceWord(screen + 0x40u));
            int c = checked((int)ReadSourceWord(screen + 0x44u));
            int f = checked((int)ReadSourceWord(screen + 0x30u));
            float mx = BitConverter.Int32BitsToSingle((int)ReadSourceWord(screen + 0x34u));
            float my = BitConverter.Int32BitsToSingle((int)ReadSourceWord(screen + 0x38u));
            if (ReadSourceWord(screen) != 0x002794A0 || ReadSourceWord(screen + 4) != 0x0027B590 ||
                r != quickRows[groupIndex] || c != quickColumns[groupIndex] ||
                f != quickFlags[groupIndex] || ReadSourceWord(screen + 0x3cu) != 0 ||
                !float.IsFinite(mx) || !float.IsFinite(my) ||
                mx is < 0 or >= 1 || my is < 0 or >= 1)
                throw new InvalidDataException($"R&C1 Quick Select grid {slot} is not the retail shape.");
            uint at = ReadSourceWord(screen + 0x48u);
            if (groupIndex == 0 && at != cellsPointer)
                throw new InvalidDataException("Quick Select does not share the native Weapons cell table.");
            var quickCells = new List<WeaponCell>(r * c);
            for (int index = 0; index < r * c; index++)
            {
                uint first = ReadSourceWord(at + (uint)(index * 10));
                uint second = ReadSourceWord(at + (uint)(index * 10 + 4));
                int id = unchecked((short)(second >> 16));
                ushort icon = unchecked((ushort)first);
                short frame = unchecked((short)(first >> 16));
                short kind = unchecked((short)second);
                if (id is < 1 or >= 37 || icon is < 0xEA60 or > 0xEAC9 || frame != 0 ||
                    kind != 0 || quickCells.Any(cell => cell.NativeId == id))
                    throw new InvalidDataException($"R&C1 Quick Select cell {slot}:{index} is invalid.");
                quickCells.Add(new WeaponCell(id, icon, frame, kind));
            }
            if (groupIndex == 0 && !quickCells.SequenceEqual(cells))
                throw new InvalidDataException("Quick Select Weapons candidates differ from native Weapons cells.");
            quickGroups.Add(new QuickSelectGroup(slot, checked((int)screen), r, c, f, mx, my,
                checked((int)ReadSourceWord(screen + 0x4cu)),
                checked((int)ReadSourceWord(screen + 0x50u)), quickCells));
        }
        if (quickFocus != (uint)quickGroups[0].ScreenAddress ||
            quickGroups[0].UpScreen != quickGroups[1].ScreenAddress ||
            quickGroups[0].DownScreen != quickGroups[1].ScreenAddress ||
            quickGroups[1].UpScreen != quickGroups[0].ScreenAddress ||
            quickGroups[1].DownScreen != quickGroups[0].ScreenAddress)
            throw new InvalidDataException("Quick Select does not have its authored two-group focus ring.");

        return new Rac1MenuPanelPages(pages, entries,
            new WeaponsGrid(checked((int)weaponsPage), checked((int)weaponsScreen),
                rows, columns, gridFlags, marginX, marginY, cells),
            new GadgetsGrid(checked((int)gadgetsPage), checked((int)gadgetFocus), gadgetGroups),
            new QuickSelectPage(checked((int)quickPage), checked((int)quickFocus), quickGroups));
    }
}
