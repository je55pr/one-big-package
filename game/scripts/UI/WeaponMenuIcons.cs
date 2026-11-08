using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Presentation;
using OBP.RAC2.Presentation;
using OBP.RAC3.Presentation;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>Player-source UI resources; decoded and authored in memory, never exported retail artwork.</summary>
internal static class WeaponMenuIcons
{
    private static readonly Dictionary<(ObpSourceGame, ushort), Texture2D> Icons = [];

    internal static void Clear(ObpSourceGame game)
    {
        foreach (var key in Icons.Keys.Where(key => key.Item1 == game).ToArray()) Icons.Remove(key);
    }

    internal static void Load(ObpSourceGame game, IRandomAccessReader? reader)
    {
        Clear(game);
        var frames = new Dictionary<ushort, RuntimeTexture>();
        if (game == ObpSourceGame.Rac1)
        {
            foreach (var weapon in Enum.GetValues<Rac1WeaponId>())
                if (Rac1WeaponIconCatalog.Ordinary(weapon) is { } icon &&
                    Rac1RetailUiAssets.HudFrame(icon.AnimationId) is { } texture)
                    frames.TryAdd(icon.AnimationId, texture);
        }
        else
        {
            if (reader is null) throw new InvalidOperationException("Weapon icons need the attached source reader.");
            var bank = game switch
            {
                ObpSourceGame.Rac2 => Rac2WeaponMenuAssets.Read(reader),
                ObpSourceGame.Rac3 => Rac3WeaponMenuAssets.Read(reader),
                _ => throw new ArgumentOutOfRangeException(nameof(game)),
            };
            foreach (var key in PlayerWeaponMenuRoster.Families.SelectMany(family => family.Members).Where(key => key.SourceGame == game))
                if (Animation(key) is { } id && !frames.ContainsKey(id) && bank.DecodeFrame(id) is { } decoded)
                    frames.Add(id, new("weapon-menu", id, decoded.Width, decoded.Height, decoded.Rgba));
        }
        var background = game == ObpSourceGame.Rac1 ? SharedBlueTile(frames.Values.ToArray()) : null;
        foreach (var (id, frame) in frames)
        {
            byte[] pixels = WhiteMask(frame, background);
            using var image = Image.CreateFromData(128, 128, false, Image.Format.Rgba8, pixels);
            Icons.Add((game, id), ImageTexture.CreateFromImage(image));
        }
        GD.Print($"[weapon-menu] {SourceIdentityWire.FormatSource(game)}: prepared {frames.Count} white weapon glyphs from attached source");
    }

    internal static Texture2D? ForFamily(PlayerWeaponMenuFamily family)
    {
        // The authored board has a wrench cell; RAC1's native icon catalog correctly
        // remains without an EA68 glyph. Keep this host illustration separate.
        if (family.Id == "wrench") return GD.Load<Texture2D>("res://assets/ui/obp-wrench.svg");
        foreach (var key in family.Members)
            if (Animation(key) is { } id && Icons.TryGetValue((key.SourceGame, id), out var texture)) return texture;
        return null;
    }

    private static ushort? Animation(SourceWeaponKey key) => key.SourceGame switch
    {
        ObpSourceGame.Rac1 => Rac1WeaponIconCatalog.Ordinary((Rac1WeaponId)key.NativeItemId)?.AnimationId,
        ObpSourceGame.Rac2 => Rac2WeaponMenuAssets.Icon(key.NativeItemId),
        ObpSourceGame.Rac3 => Rac3WeaponMenuAssets.Icon(key.NativeItemId),
        _ => null,
    };

    // OBP-authored matte recovery. The fifteen normal RAC1 weapon glyphs have
    // complementary shapes over one shared blue gradient, exposing every background texel.
    private static byte[] SharedBlueTile(IReadOnlyList<RuntimeTexture> frames)
    {
        if (frames.Count != 15 || frames.Any(frame => frame.Width != 32 || frame.Height != 32))
            throw new InvalidDataException("RAC1's complete normal weapon glyph set is required for background separation.");
        var background = new byte[32 * 32 * 4];
        for (int at = 0; at < background.Length; at += 4)
        {
            var source = frames.MaxBy(frame => Math.Max(frame.Rgba[at], Math.Max(frame.Rgba[at + 1], frame.Rgba[at + 2])))!;
            Array.Copy(source.Rgba, at, background, at, 4);
        }
        return background;
    }

    private static byte[] WhiteMask(RuntimeTexture frame, byte[]? background)
    {
        int width = frame.Width, height = frame.Height;
        var coverage = new double[width * height];
        for (int i = 0; i < coverage.Length; i++)
            coverage[i] = background is null ? frame.Rgba[i * 4 + 3] / 255d : MatteCoverage(frame.Rgba, background, i * 4);
        if (background is not null) RemoveSpeckles(coverage, width, height);
        return FitMask(coverage, width, height);
    }

    private static double MatteCoverage(byte[] pixels, byte[] background, int at)
    {
        double numerator = 0, denominator = 0;
        for (int ch = 0; ch < 3; ch++)
        {
            double direction = background[at + ch] - 15d;
            numerator += (background[at + ch] - pixels[at + ch]) * direction;
            denominator += direction * direction;
        }
        return Math.Clamp((numerator / Math.Max(1, denominator) - .06) / .88, 0, 1);
    }

    private static void RemoveSpeckles(double[] coverage, int width, int height)
    {
        bool[] core = coverage.Select(value => value >= .25).ToArray();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (!NearCore(core, width, height, x, y)) coverage[y * width + x] = 0;
    }

    private static bool NearCore(bool[] core, int width, int height, int x, int y)
    {
        for (int row = Math.Max(0, y - 1); row <= Math.Min(height - 1, y + 1); row++)
            for (int column = Math.Max(0, x - 1); column <= Math.Min(width - 1, x + 1); column++)
                if (core[row * width + column]) return true;
        return false;
    }

    private static byte[] FitMask(double[] alpha, int width, int height)
    {
        var (left, top, right, bottom) = MaskBounds(alpha, width, height);
        if (right <= left || bottom <= top) throw new InvalidDataException("Weapon glyph has no mask coverage.");
        double scale = Math.Min(112d / (right - left), 112d / (bottom - top));
        int w = Math.Max(1, (int)Math.Round((right - left) * scale)), h = Math.Max(1, (int)Math.Round((bottom - top) * scale));
        var result = new byte[128 * 128 * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx = Math.Min(right - 1, left + (int)((x + .5) * (right - left) / w));
                int sy = Math.Min(bottom - 1, top + (int)((y + .5) * (bottom - top) / h));
                byte a = (byte)Math.Round(alpha[sy * width + sx] * 255);
                if (a == 0) continue;
                int at = (((128 - h) / 2 + y) * 128 + (128 - w) / 2 + x) * 4;
                result[at] = result[at + 1] = result[at + 2] = 255; result[at + 3] = a;
            }
        return result;
    }

    private static (int Left, int Top, int Right, int Bottom) MaskBounds(double[] alpha, int width, int height)
    {
        int left = width, top = height, right = 0, bottom = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (Math.Round(alpha[y * width + x] * 255) > 0)
                { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); }
        return (left, top, right, bottom);
    }
}
