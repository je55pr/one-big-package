using OBP.PS2.Geometry;

namespace OBP.RAC2.Geometry;

/// <summary>
/// Going Commando compatibility facade over the retail-validated shared
/// <see cref="RcTfrag"/> inner terrain codec. GC's enclosing WAD/chunk layout
/// remains game-specific; keeping this facade preserves the existing RAC2 API
/// and equivalence tests while allowing R&amp;C1 to consume the shared codec
/// without depending on OBP.RAC2.
/// </summary>
public static class GcTfrag
{
    public const int TfragHeaderSize = RcTfrag.TfragHeaderSize;

    public sealed record Mesh(
        double[] Positions,
        float[] Uvs,
        float[] Colors,
        int[] Indices,
        int[] TriangleTextureIds,
        int TfragCount,
        (double X, double Y, double Z) BoundsMin,
        (double X, double Y, double Z) BoundsMax,
        IReadOnlyList<int> TextureIds);

    public sealed record Options(int MaxVertices = 12_000_000, int MaxTriangles = 12_000_000, bool ToYUp = true);

    public static Mesh Read(byte[] blob, Options? options = null)
    {
        options ??= new Options();
        var mesh = RcTfrag.Read(blob, new RcTfrag.Options(options.MaxVertices, options.MaxTriangles, options.ToYUp));
        return new Mesh(
            mesh.Positions,
            mesh.Uvs,
            mesh.Colors,
            mesh.Indices,
            mesh.TriangleTextureIds,
            mesh.TfragCount,
            mesh.BoundsMin,
            mesh.BoundsMax,
            mesh.TextureIds);
    }
}
