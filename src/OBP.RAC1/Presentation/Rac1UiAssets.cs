using System.Buffers.Binary;
using System.Text;
using OBP.IO;
using OBP.PS2;
using OBP.PS2.Elf;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.RAC1.Presentation;

/// <summary>UI assets read directly from the supported NTSC-U disc; no exported files or host engine required.</summary>
public sealed record Rac1UiAssets(Rac1HudBank Hud, IReadOnlyDictionary<string, Rac1BitmapFont> Fonts,
    IReadOnlyList<IReadOnlyDictionary<uint, byte[]>> Languages)
{
    // Original level-local Moby class 0x472. Projected panel geometry and
    // animation frames stay source-owned, not invented Godot meshes.
    public Rac1MenuPanelModel? NativeMenuPanels { get; init; }
    public Rac1MenuPanelPages? NativeMenuPages { get; init; }

    // Per-level resident PS2 effect textures referenced by the retail menu's
    // flashing panel (0x19/0x1A/0x1C); these are decoded only from the user disc.
    public IReadOnlyDictionary<int, OBP.Runtime.RuntimeTexture> MenuEffects { get; init; } =
        new Dictionary<int, OBP.Runtime.RuntimeTexture>();
    public IReadOnlyDictionary<int, OBP.Runtime.Audio.RuntimeAudioClip> MenuSounds { get; init; } =
        new Dictionary<int, OBP.Runtime.Audio.RuntimeAudioClip>();
    public static Rac1UiAssets Read(IRandomAccessReader disc)
    {
        var boot = Ps2Boot.OpenDisc(disc);
        if (boot.Boot.Serial != Rac1Authority.Primary.Serial || disc.Length != Rac1Authority.PrimaryIsoSizeBytes)
            throw new InvalidDataException("R&C1 UI assets require the supported NTSC-U original disc.");
        var catalogue = Rac1DiscIndex.Read(disc);
        byte[] index = disc.Read((long)Rac1DiscIndex.IndexLba * Rac1DiscIndex.SectorBytes, Rac1DiscIndex.IndexSize);
        var hud = Rac1HudBank.Read(disc, index);
        var core = Rac1LevelCore.Open(disc, catalogue.Levels.Single(level => level.LevelId == 0));
        var atlases = Rac1FxTextures.Read(core, new HashSet<int> { 1, 2, 3, 0x19, 0x1A, 0x1C });
        var segments = Ps2Boot.ReadBootProgramHeaders(boot);
        (string Style, int Id, long Address)[] tables = [("regular", 1, 0x1df050), ("small", 2, 0x1df3f0), ("large", 3, 0x1df790)];
        var fonts = tables.ToDictionary(t => t.Style, t => Rac1BitmapFont.Decode(t.Style, atlases[t.Id],
            Elf32Reader.ReadVirtualRange(boot.BootExecutable, segments, t.Address, Rac1BitmapFont.GlyphCount * 4)));
        return new Rac1UiAssets(hud, fonts, DecodeLanguages(ReadArchive(disc, index, 0x1528, 2 * 1024 * 1024)))
        {
            MenuEffects = atlases.Where(pair => pair.Key is 0x19 or 0x1A or 0x1C)
                .ToDictionary(pair => pair.Key, pair => pair.Value),
            MenuSounds = Rac1MenuAudioBank.Read(disc, index),
            NativeMenuPanels = Rac1MenuPanelModel.Read(core),
            NativeMenuPages = Rac1MenuPanelPages.Read(disc,
                catalogue.Levels.Single(level => level.LevelId == 0)),
        };
    }

    internal static byte[] ReadArchive(IRandomAccessReader disc, byte[] index, int field, int cap)
    {
        uint lba = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(field, 4));
        uint sectors = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(field + 4, 4));
        long length = (long)sectors * Rac1DiscIndex.SectorBytes;
        long offset = (long)lba * Rac1DiscIndex.SectorBytes;
        if (length <= 0 || length > cap || offset > disc.Length || length > disc.Length - offset)
            throw new InvalidDataException("R&C1 UI archive exceeds the disc or its size limit.");
        return disc.Read(offset, (int)length);
    }

    /// <summary>Preserve native bytes, including colour/button controls; these tables are not UTF-8.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<uint, byte[]>> DecodeLanguages(byte[] archive)
    {
        if (archive.Length < 24 || archive.Length > 2 * 1024 * 1024) throw new InvalidDataException("Invalid R&C1 localisation archive size.");
        var languages = new List<IReadOnlyDictionary<uint, byte[]>>();
        for (int language = 0; language < 6; language++)
        {
            long start = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(language * 4, 4));
            if (start < 24 || start > archive.Length - 8) throw new InvalidDataException("R&C1 localisation header is outside the archive.");
            int at = (int)start;
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(at, 4));
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(at + 4, 4));
            if (count > 5000 || length > archive.Length - start || 8L + count * 16L > length)
                throw new InvalidDataException("R&C1 localisation table has invalid bounds.");
            var entries = new Dictionary<uint, byte[]>();
            for (int i = 0; i < count; i++)
            {
                int row = at + 8 + i * 16;
                uint relative = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(row, 4));
                uint id = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(row + 4, 4));
                if (relative < 8 + count * 16 || relative >= length)
                    throw new InvalidDataException("R&C1 text points outside its language's string data.");
                var tail = archive.AsSpan(at + (int)relative, (int)Math.Min(4097, length - relative));
                int terminator = tail.IndexOf((byte)0);
                if (terminator is < 0 or > 4096) throw new InvalidDataException("R&C1 text is unterminated or exceeds its length limit.");
                if (!entries.TryAdd(id, tail[..terminator].ToArray())) throw new InvalidDataException("Duplicate R&C1 text identity.");
            }
            languages.Add(entries);
        }
        return languages;
    }

    public string? Text(uint id, int language = 0) =>
        (uint)language < Languages.Count && Languages[language].TryGetValue(id, out var raw) ? Encoding.Latin1.GetString(raw) : null;
}

public sealed record Rac1BitmapFont(string Style, RuntimeTexture Atlas, IReadOnlyList<Rac1BitmapFont.Glyph> Glyphs)
{
    public const int GlyphCount = 232;
    public sealed record Glyph(int Code, byte U, byte V, sbyte Top, sbyte Advance)
    {
        public bool Printable => Code >= 32 && Advance > 0;
    }

    public static Rac1BitmapFont Decode(string style, RuntimeTexture atlas, byte[] metrics)
    {
        if (metrics.Length != GlyphCount * 4 || atlas.Width != 256 || atlas.Height != 128 || atlas.Rgba.Length != 256 * 128 * 4)
            throw new InvalidDataException("R&C1 bitmap font atlas or glyph table is invalid.");
        var glyphs = new Glyph[GlyphCount];
        for (int i = 0; i < glyphs.Length; i++)
        {
            var g = new Glyph(i, metrics[i * 4], metrics[i * 4 + 1], unchecked((sbyte)metrics[i * 4 + 2]), unchecked((sbyte)metrics[i * 4 + 3]));
            if (g.Printable && (g.U + 16 > atlas.Width || g.V + 16 > atlas.Height))
                throw new InvalidDataException("R&C1 bitmap glyph exceeds its atlas.");
            glyphs[i] = g;
        }
        return new(style, atlas, glyphs);
    }
}
