using System.Numerics;

namespace OBP.RAC1.Level;

/// <summary>
/// The per-vertex surface of one pool tile, as the native grid routines build it each frame
/// (0x2b8c08 positions, 0x261df8 normals / grey light / reflection coordinates, 0x2b80d0 scroll
/// offsets). Engine-neutral: native Z-up space, 17 x 17 vertices, row-major.
/// </summary>
public static class Rac1PoolWaterSurface
{
    public const int Vertices = Rac1NovalisPoolWater.Cells + 1;
    public const int VertexCount = Vertices * Vertices;

    // Constants of 0x261df8 (vf14): 0.5, 65536.5 (the mantissa trick that turns 0.5 + x into a byte), 0.573, 3.25.
    private static readonly float LightBase = BitConverter.Int32BitsToSingle(0x3f12b021);   // vf14.z
    private const float LightGain = 3.25f;           // 0x40500000
    private const float ReflectionScale = 0.9f;      // pool_light +0x18 (set by the controller's first update)
    private const float SunComponent = 0.57735f;     // the controller's light vector magnitude

    /// <summary>The per-frame, per-tile texture-coordinate scroll: first and second checkerboard offsets.</summary>
    public readonly record struct Scroll(float FirstU, float FirstV, float SecondU, float SecondV);

    /// <summary>
    /// <c>update_animated_offset_pairs</c> after <paramref name="frame"/> frames: the tile's u / v scroll
    /// (wrapped to 0..1) plus the two swells, a circle of radius 0.03 per phase.
    /// </summary>
    public static Scroll ScrollAt(Rac1NovalisPoolWater.Tile tile, long frame)
    {
        double u = Frac(Rac1NovalisPoolWater.ScrollPerFrameU * (double)frame);
        double v = Frac(tile.ScrollV * (double)frame);
        double a0 = Rac1NovalisPoolWater.SwellPhase0 + Rac1NovalisPoolWater.SwellStep0 * (double)frame;
        double a1 = Rac1NovalisPoolWater.SwellPhase1 + Rac1NovalisPoolWater.SwellStep1 * (double)frame;
        double r = Rac1NovalisPoolWater.SwellRadius;
        return new((float)(u + Math.Cos(a0) * r), (float)(v + Math.Sin(a0) * r),
                   (float)(u + Math.Cos(a1) * r), (float)(v + Math.Sin(a1) * r));
    }

    private static double Frac(double value) => value - Math.Floor(value);

    /// <summary>One filled vertex array set; reuse it between frames.</summary>
    public sealed class Buffers
    {
        public readonly Vector3[] Positions = new Vector3[VertexCount];
        public readonly byte[] Grey = new byte[VertexCount];
        public readonly Vector2[] ReflectionUv = new Vector2[VertexCount];
    }

    /// <summary>
    /// Fills <paramref name="buffers"/> for <paramref name="tile"/>. <paramref name="sunAzimuth"/> is the
    /// heading of the light the controller samples when it first runs.
    /// </summary>
    public static void Build(Rac1NovalisPoolWater.Tile tile, Rac1PoolWaterSimulation simulation,
        Vector3 camera, float sunAzimuth, Buffers buffers)
    {
        var light = new Vector3(MathF.Cos(sunAzimuth) * SunComponent, MathF.Sin(sunAzimuth) * SunComponent, -SunComponent);
        float step = Rac1NovalisPoolWater.CellSize;
        for (int j = 0; j < Vertices; j++)
            for (int i = 0; i < Vertices; i++)
            {
                int index = j * Vertices + i;
                float centre = simulation.RenderHeight(tile.Index, i, j);
                float height = centre;
                // Flagged edges are forced flat at the tile height.
                if (((tile.FlatEdges & 1) != 0 && j == 0) || ((tile.FlatEdges & 2) != 0 && i == Vertices - 1) ||
                    ((tile.FlatEdges & 4) != 0 && j == Vertices - 1) || ((tile.FlatEdges & 8) != 0 && i == 0))
                    height = 0f;
                var position = new Vector3(
                    tile.X + Rac1NovalisPoolWater.TileOrigin + i * step,
                    tile.Y + Rac1NovalisPoolWater.TileOrigin + j * step,
                    tile.Z + height);
                buffers.Positions[index] = position;

                float left = simulation.RenderHeight(tile.Index, i - 1, j), right = simulation.RenderHeight(tile.Index, i + 1, j);
                float up = simulation.RenderHeight(tile.Index, i, j - 1), down = simulation.RenderHeight(tile.Index, i, j + 1);
                // Two edge-vector pairs around the vertex, exactly as 0x261df8 forms them (including the
                // sign conventions of its neighbour differences), summed and normalised.
                var a = new Vector3(0, -step, centre - down);
                var b = new Vector3(-step, step, down - right);
                var c = new Vector3(0, step, centre - up);
                var d = new Vector3(step, -step, up - left);
                var sum = Vector3.Cross(b, a) + Vector3.Cross(d, c);
                float length = sum.Length();
                var unit = length > 0f ? sum / length : Vector3.UnitZ;
                var negated = -unit;

                float facing = Vector3.Dot(light, negated);
                float x = Math.Clamp((facing - LightBase) * LightGain + LightBase, 0f, 1f);
                // 65536.5 + x truncated to its low mantissa byte: 64 + floor(128 x).
                buffers.Grey[index] = (byte)(64 + (int)(x * 128f));

                var toCamera = camera - position;
                float distance = toCamera.Length();
                var view = distance > 0f ? toCamera / distance : Vector3.UnitZ;
                var incident = -view;
                float dot = Vector3.Dot(incident, negated);
                Vector3 reflected = dot >= 0f ? incident + 2f * (negated * dot - incident) : view;
                buffers.ReflectionUv[index] = new Vector2(
                    0.5f + reflected.X * ReflectionScale * 0.5f, 0.5f + reflected.Y * ReflectionScale * 0.5f);
            }
    }
}
