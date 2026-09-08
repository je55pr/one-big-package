using System.Buffers.Binary;
using OBP.PS2.Geometry;
using OBP.PS2.Textures;

namespace OBP.RAC3.Level;

/// <summary>Retail-corroborated UYA level textures and static TIE/shrub class geometry.</summary>
public static class UyaAssets
{
    public enum TextureTable { Tfrag, Moby, Tie, Shrub }
    public sealed record StaticClass(int OClass, double[] Positions, float[] Uvs, int[] Indices, int[] TriangleTextureIds);
    public sealed record MobyVisualClass(
        int OClass,
        GcUyaMoby.Mesh Mesh,
        int[] TriangleTextureIds,
        IReadOnlyList<GcUyaMoby.MobyJoint> Joints,
        IReadOnlyList<GcUyaMoby.MobySequence> Sequences);
    public sealed record MobyClassSet(
        int DeclaredCount,
        IReadOnlyDictionary<int, MobyVisualClass> Decoded,
        IReadOnlySet<int> ZeroLocalCoreClasses);

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

    public static MobyClassSet ReadMobyClasses(UyaLevelCore.Core core, int textureCount)
    {
        var table = core.Header.MobyClasses;
        if (table.Count < 0 || table.Offset < 0 ||
            (long)table.Offset + (long)table.Count * GcUyaMoby.ClassEntrySize > core.Index.Length)
        {
            throw new InvalidDataException("UYA Moby class table lies outside the core index.");
        }

        var decoded = new Dictionary<int, MobyVisualClass>();
        var zeroLocalCore = new HashSet<int>();
        var seen = new HashSet<int>();
        for (int i = 0; i < table.Count; i++)
        {
            int at = table.Offset + i * GcUyaMoby.ClassEntrySize;
            int assetOffset = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at));
            int oClass = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at + 4));
            if (!seen.Add(oClass))
                throw new InvalidDataException($"UYA Moby class table repeats oClass {oClass}.");
            if (assetOffset == 0)
            {
                zeroLocalCore.Add(oClass);
                continue;
            }
            if (assetOffset < 0 || assetOffset + GcUyaMoby.ClassHeaderSize > core.Assets.Length)
                throw new InvalidDataException($"UYA Moby class {oClass} has invalid asset offset 0x{assetOffset:x}.");

            int end = core.SectionBoundaries.FirstOrDefault(b => b > assetOffset, core.Assets.Length);
            if (end <= assetOffset)
                throw new InvalidDataException($"UYA Moby class {oClass} has no bounded asset extent.");
            byte[] classBytes = core.Assets.AsSpan(assetOffset, end - assetOffset).ToArray();
            var mesh = GcUyaMoby.ReadClass(classBytes);
            if (mesh.Positions.Any(v => !double.IsFinite(v)))
                throw new InvalidDataException($"UYA Moby class {oClass} contains non-finite geometry.");
            if (mesh.TriangleMaterialSlots.Length != mesh.Indices.Length / 3)
                throw new InvalidDataException($"UYA Moby class {oClass} has inconsistent triangle materials.");

            byte[] textureIds = core.Index.AsSpan(at + 0x10, 16).ToArray();
            var mapped = new int[mesh.TriangleMaterialSlots.Length];
            for (int face = 0; face < mapped.Length; face++)
            {
                int slot = mesh.TriangleMaterialSlots[face];
                if (slot == -1)
                {
                    mapped[face] = -1;
                    continue;
                }
                if (slot < 0 || slot >= textureIds.Length)
                    throw new InvalidDataException($"UYA Moby class {oClass} triangle {face} references invalid material slot {slot}.");
                int textureId = textureIds[slot];
                if (textureId != 0xff && (textureId < 0 || textureId >= textureCount))
                    throw new InvalidDataException($"UYA Moby class {oClass} references texture {textureId} outside 0..{textureCount - 1}.");
                mapped[face] = textureId;
            }
            int jointCount = classBytes[0x08];
            var joints = GcUyaMoby.ReadJoints(classBytes, jointCount);
            var sequences = joints.Count > 0 ? GcUyaMoby.ReadSequences(classBytes, jointCount) : [];
            decoded.Add(oClass, new MobyVisualClass(oClass, mesh, mapped, joints, sequences));
        }

        if (decoded.Count + zeroLocalCore.Count != table.Count)
            throw new InvalidDataException($"UYA Moby table declares {table.Count} classes but classified {decoded.Count + zeroLocalCore.Count}.");
        return new MobyClassSet(table.Count, decoded, zeroLocalCore);
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
