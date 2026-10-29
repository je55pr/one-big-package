using OBP.RAC1.Level;

namespace OBP.RAC1.Presentation;

/// <summary>
/// Native Aridia ocean surface draw input, before VU1 material and clipping.
/// Preserves the source 8x8 XY grid, 124-entry triangle-strip order,
/// separately authored ST texture coordinates, and packed RGB samples.
/// No alpha, fog, animation or shader semantics are invented here.
/// </summary>
public static class Rac1AridiaOceanGeometry
{
    public sealed record Surface(
        uint SourceAddress,
        double[] PositionsYUp,
        float[] TextureST,
        byte[] PackedRgb,
        int[] TriangleIndices)
    {
        public int VertexCount => PositionsYUp.Length / 3;
        public int TriangleCount => TriangleIndices.Length / 3;
    }

    public static IReadOnlyList<Surface> Read(Rac1LevelOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var records = Rac1AridiaOceanSource.Decode(overlay);
        byte[] lattice = Rac1AridiaOceanSource.ReadStripLatticeIndices(overlay);
        var textureST = Rac1AridiaOceanSource.ReadStripTextureCoordinates(overlay);
        var output = new List<Surface>(records.Count);

        foreach (var record in records)
        {
            var sourceCells = Rac1AridiaOceanSource.ReadCellPackets(overlay, record);
            var positions = new List<double>();
            var st = new List<float>();
            var rgb = new List<byte>();
            var triangles = new List<int>();

            for (int row = 0; row < record.CellRows; row++)
            {
                for (int col = 0; col < record.CellColumns; col++)
                {
                    int cellIndex = row * record.CellColumns + col;
                    if (record.CellMask[cellIndex] == 0)
                        continue;
                    int baseIndex = positions.Count / 3;
                    var strip = Rac1AridiaOceanSource.DecodeCellStrip(sourceCells[cellIndex], lattice);
                    for (int i = 0; i < strip.Length; i++)
                    {
                        var vertex = strip[i];
                        int x = vertex.LatticeIndex % 8;
                        int y = vertex.LatticeIndex / 8;
                        positions.Add(record.OriginX + (col * 7 + x) * record.StepX);
                        positions.Add(record.Height); // Native Z-up into neutral Y-up.
                        positions.Add(record.OriginY + (row * 7 + y) * record.StepY);
                        st.Add(textureST[i].X);
                        st.Add(textureST[i].Y);
                        rgb.Add(vertex.Red);
                        rgb.Add(vertex.Green);
                        rgb.Add(vertex.Blue);
                    }

                    // Native strip contains degenerate row-join vertices.
                    // Keep all 124 vertex attributes in source order and only
                    // suppress those joins when emitting triangles.
                    for (int i = 2; i < strip.Length; i++)
                    {
                        byte a = strip[i - 2].LatticeIndex;
                        byte b = strip[i - 1].LatticeIndex;
                        byte c = strip[i].LatticeIndex;
                        if (a == b || a == c || b == c)
                            continue;

                        bool odd = (i & 1) != 0;
                        triangles.Add(baseIndex + (odd ? i - 1 : i - 2));
                        triangles.Add(baseIndex + (odd ? i - 2 : i - 1));
                        triangles.Add(baseIndex + i);
                    }
                }
            }

            output.Add(new Surface(record.SourceAddress,
                positions.ToArray(), st.ToArray(), rgb.ToArray(), triangles.ToArray()));
        }
        return output;
    }
}