using System.Buffers.Binary;
using OBP.PS2.Geometry;
using OBP.RAC2.Level;

namespace OBP.RAC2.Geometry;

/// <summary>Going Commando facade over the shared RC Tie packet codec.</summary>
public static class GcTie
{
    public const int ClassHeaderSize = RcTie.GcClassHeaderSize;
    public const int ClassEntrySize = RcTie.ClassEntrySize;

    public sealed record Mesh(
        double[] Positions,
        float[] Uvs,
        int[] Indices,
        int[] TriangleMaterialSlots,
        float Scale);

    public sealed record TieClass(
        int OClass,
        int AssetOffset,
        Mesh Mesh,
        int[] TriangleTextureIds,
        IReadOnlyList<int> TextureIds);

    public static Mesh ReadClass(ReadOnlySpan<byte> buf)
    {
        var mesh = RcTie.ReadClass(buf, RcTie.GcLayout);
        return new Mesh(mesh.Positions, mesh.Uvs, mesh.Indices, mesh.TriangleMaterialSlots, mesh.Scale);
    }
    public static Dictionary<int, TieClass> ReadClasses(GcLevelCore.Core core)
    {
        var table = core.Header.TieClasses;
        var outp = new Dictionary<int, TieClass>();
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
                    .Select(slot => slot >= 0 && slot < textureIds.Length ? textureIds[slot] : -1)
                    .ToArray(); outp[oClass] = new TieClass(
                    oClass,
                    assetOffset,
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
