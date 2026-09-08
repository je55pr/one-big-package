using System.Buffers.Binary;
using OBP.PS2.Geometry;
using OBP.RAC2.Level;

namespace OBP.RAC2.Geometry;

/// <summary>Going Commando facade over the shared RC Shrub codec.</summary>
public static class GcShrub
{
    public const int ClassHeaderSize = RcShrub.ClassHeaderSize;
    public const int ClassEntrySize = RcShrub.ClassEntrySize;

    public sealed record Mesh(
        double[] Positions,
        float[] Uvs,
        int[] Indices,
        int[] TriangleMaterialSlots,
        float Scale);

    public sealed record ShrubClass(
        int OClass,
        Mesh Mesh,
        int[] TriangleTextureIds,
        IReadOnlyList<int> TextureIds);

    public static Mesh ReadClass(ReadOnlySpan<byte> buf)
    {
        var mesh = RcShrub.ReadClass(buf);
        return new Mesh(mesh.Positions, mesh.Uvs, mesh.Indices, mesh.TriangleMaterialSlots, mesh.Scale);
    }
    public static Dictionary<int, ShrubClass> ReadClasses(GcLevelCore.Core core)
    {
        var table = core.Header.ShrubClasses;
        var outp = new Dictionary<int, ShrubClass>();
        for (int i = 0; i < table.Count; i++)
        {
            int at = table.Offset + i * ClassEntrySize;
            if (at < 0 || at + ClassEntrySize > core.Index.Length)
            {
                break;
            }

            int assetOffset = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at));
            int oClass = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at + 4));
            if (assetOffset <= 0 || assetOffset >= core.Assets.Length)
            {
                continue;
            }

            var textureIds = core.Index.AsSpan(at + 0x10, 16).ToArray();
            int end = core.SectionBoundaries.FirstOrDefault(b => b > assetOffset, core.Assets.Length);
            try
            {
                var mesh = ReadClass(core.Assets.AsSpan(assetOffset, end - assetOffset));
                var mapped = mesh.TriangleMaterialSlots
                    .Select(slot => slot >= 0 && slot < textureIds.Length ? textureIds[slot] : slot)
                    .ToArray(); outp[oClass] = new ShrubClass(
                    oClass,
                    mesh,
                    mapped,
                    mapped.Where(id => id >= 0).Distinct().OrderBy(id => id).ToList());
            }
            catch
            {
                // Preserve the established GC compatibility behavior.
            }
        }

        return outp;
    }
}
