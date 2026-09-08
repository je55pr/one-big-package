using System.Buffers.Binary;
using OBP.PS2.Geometry;
using OBP.PS2.Textures;

namespace OBP.RAC3.Level;

/// <summary>Retail-corroborated UYA level textures and static TIE/shrub class geometry.</summary>
public static class UyaAssets
{
    public enum TextureTable { Tfrag, Moby, Tie, Shrub }
    public sealed record StaticClass(int OClass, double[] Positions, float[] Uvs, int[] Indices, int[] TriangleTextureIds);

    public static IReadOnlyList<RcLevelTextureTable.Texture> ReadTextures(UyaLevelCore.Core core, TextureTable table)
    {
        UyaLevelCore.ArrayRange r = table switch
        {
            TextureTable.Tfrag => core.Header.TfragTextures,
            TextureTable.Moby => core.Header.MobyTextures,
            TextureTable.Tie => core.Header.TieTextures,
            TextureTable.Shrub => core.Header.ShrubTextures,
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
        return RcLevelTextureTable.Read(core.Index, core.Assets, core.GsRam, core.Header.TexturesBaseOffset,
            new RcLevelTextureTable.Range(r.Count, r.Offset), new RcLevelTextureTable.Options(1024, Strict: true));
    }

    public static IReadOnlyDictionary<int, StaticClass> ReadTieClasses(UyaLevelCore.Core core)
        => ReadClasses(core, core.Header.TieClasses, RcTie.ClassEntrySize, 0x80,
            bytes =>
            {
                var m = RcTie.ReadClass(bytes, RcTie.GcLayout);
                return (m.Positions, m.Uvs, m.Indices, m.TriangleMaterialSlots);
            }, "TIE");

    public static IReadOnlyDictionary<int, StaticClass> ReadShrubClasses(UyaLevelCore.Core core)
        => ReadClasses(core, core.Header.ShrubClasses, RcShrub.ClassEntrySize, 0x40,
            bytes =>
            {
                var m = RcShrub.ReadClass(bytes);
                return (m.Positions, m.Uvs, m.Indices, m.TriangleMaterialSlots);
            }, "shrub");

    private static IReadOnlyDictionary<int, StaticClass> ReadClasses(
        UyaLevelCore.Core core, UyaLevelCore.ArrayRange table, int stride, int minimumHeader,
        Func<byte[], (double[] P, float[] U, int[] I, int[] Slots)> decode, string label)
    {
        if (table.Count < 0 || table.Offset < 0 || (long)table.Offset + (long)table.Count * stride > core.Index.Length)
            throw new InvalidDataException($"UYA {label} class table lies outside the core index.");
        var result = new Dictionary<int, StaticClass>();
        for (int i = 0; i < table.Count; i++)
        {
            int at = table.Offset + i * stride;
            int assetOffset = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at));
            int oClass = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at + 4));
            if (assetOffset <= 0 || assetOffset + minimumHeader > core.Assets.Length)
                throw new InvalidDataException($"UYA {label} class {oClass} has invalid asset offset 0x{assetOffset:x}.");
            if (result.ContainsKey(oClass)) throw new InvalidDataException($"UYA {label} class table repeats oClass {oClass}.");
            int end = core.SectionBoundaries.FirstOrDefault(b => b > assetOffset, core.Assets.Length);
            if (end <= assetOffset) throw new InvalidDataException($"UYA {label} class {oClass} has no bounded asset extent.");
            var (p, u, ind, slots) = decode(core.Assets.AsSpan(assetOffset, end - assetOffset).ToArray());
            if (slots.Length != ind.Length / 3) throw new InvalidDataException($"UYA {label} class {oClass} has inconsistent triangle materials.");
            byte[] textureIds = core.Index.AsSpan(at + 0x10, 16).ToArray();
            int[] mapped = slots.Select(slot => slot >= 0 && slot < textureIds.Length ? textureIds[slot] : -1).ToArray();
            if (p.Any(v => !double.IsFinite(v))) throw new InvalidDataException($"UYA {label} class {oClass} contains non-finite geometry.");
            result.Add(oClass, new StaticClass(oClass, p, u, ind, mapped));
        }
        if (result.Count != table.Count) throw new InvalidDataException($"UYA {label} declared {table.Count} classes but decoded {result.Count}.");
        return result;
    }
}
