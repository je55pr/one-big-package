using System.Numerics;

namespace OBP.RAC1.Level;

/// <summary>
/// The pool height-field simulation recovered from the shared water-tile routines of the level
/// overlay: <c>pool_flush</c> (0x2b7fe0) runs a wave step every eighth frame and blends the two newest
/// height slots in between, the step (0x262038) is <c>h' = 0.9375 * (0.25 * sum8(h) - h_old)</c> over a
/// 16x16 cell grid with edge cells mirrored between abutting tiles, and splashes follow
/// <c>region_height_splash</c> (0x2a3ec8). Engine-neutral: native Z-up units, no Godot types.
/// </summary>
public sealed class Rac1PoolWaterSimulation
{
    private const int Cells = Rac1NovalisPoolWater.Cells;
    private const float StepFraction = 0.1f;                // pool_light +0x20
    private const float Damping = 1f / 16f;                 // 0x3d800000 in 0x262038
    private const float Quarter = 0.25f;                    // 0x3e800000

    private readonly IReadOnlyList<Rac1NovalisPoolWater.Tile> _tiles;
    private readonly float[][][] _heights;   // [tile][slot][row * 16 + column]
    private int _source;
    private int _next = 1;
    private float _accumulator = BitConverter.Int32BitsToSingle(0x3fcccccd);   // 1.6: the first call steps
    private float _blendNext = 1f;
    private float _blendSource;

    public Rac1PoolWaterSimulation(IReadOnlyList<Rac1NovalisPoolWater.Tile> tiles)
    {
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        _heights = new float[tiles.Count][][];
        for (int i = 0; i < tiles.Count; i++)
        {
            _heights[i] = new float[3][];
            for (int slot = 0; slot < 3; slot++) _heights[i][slot] = new float[Cells * Cells];
        }
    }

    /// <summary>One game frame of <c>pool_flush</c>: step every eighth frame, otherwise blend.</summary>
    public void Flush(Func<int, bool> tileActive)
    {
        if (1f - StepFraction <= _accumulator)
        {
            _accumulator = StepFraction;
            Step(tileActive);
            _blendNext = 1f;     // the render slot is the older of the two, until the accumulator advances
            _blendSource = 0f;
        }
        else
        {
            _blendNext = 1f - _accumulator;
            _blendSource = _accumulator;
        }
        _accumulator += StepFraction;
    }

    private void Step(Func<int, bool> tileActive)
    {
        for (int i = 0; i < _tiles.Count; i++)
        {
            if (!tileActive(i)) continue;
            var output = _heights[i][_next];
            for (int r = 0; r < Cells; r++)
                for (int c = 0; c < Cells; c++)
                {
                    float sum = At(i, c - 1, r - 1, _source) + At(i, c, r - 1, _source) + At(i, c + 1, r - 1, _source) +
                                At(i, c - 1, r, _source) + At(i, c + 1, r, _source) +
                                At(i, c - 1, r + 1, _source) + At(i, c, r + 1, _source) + At(i, c + 1, r + 1, _source);
                    float value = sum * Quarter - output[r * Cells + c];
                    output[r * Cells + c] = value - value * Damping;
                }
        }
        (_source, _next) = (_next, _source);
    }

    // Neighbour byte for a (dx, dy) offset, indexed (dy + 1) * 3 + (dx + 1): y-16, x-16, y+16, x+16, then the diagonals.
    private static readonly int[] DirectionIndex = [4, 0, 5, 1, -1, 3, 6, 2, 7];

    private float At(int tile, int column, int row, int slot)
    {
        int dx = column < 0 ? -1 : column >= Cells ? 1 : 0;
        int dy = row < 0 ? -1 : row >= Cells ? 1 : 0;
        int owner = tile;
        if (dx != 0 || dy != 0)
        {
            int direction = DirectionIndex[(dy + 1) * 3 + (dx + 1)];
            byte neighbour = _tiles[tile].Neighbours[direction];
            if (neighbour == 0xff || neighbour >= _tiles.Count) return 0f;   // no neighbour: the halo stays zero
            owner = neighbour;
        }
        return _heights[owner][slot][((row + Cells) % Cells) * Cells + (column + Cells) % Cells];
    }

    /// <summary>The height drawn at a grid vertex: columns / rows -1..17, taken from abutting tiles outside 0..15.</summary>
    public float RenderHeight(int tile, int column, int row) =>
        _blendNext * At(tile, column, row, _next) + _blendSource * At(tile, column, row, _source);

    /// <summary>
    /// <c>region_height_splash</c>: sets (add = false: keeps the larger magnitude) or adds
    /// <c>amount / distance</c> (full amount inside one unit) to the cells within <paramref name="radius"/>
    /// of the native position. Adds start from the currently drawn height.
    /// </summary>
    public void Splash(float x, float y, float radius, float amount, bool add, int firstTile = 0, int tileCount = int.MaxValue)
    {
        int last = (int)Math.Min((long)_tiles.Count, (long)firstTile + tileCount);
        for (int i = firstTile; i < last; i++)
        {
            var tile = _tiles[i];
            float originY = tile.Y + Rac1NovalisPoolWater.TileOrigin;
            float originX = tile.X + Rac1NovalisPoolWater.TileOrigin;
            float gy = (y - originY) / Rac1NovalisPoolWater.CellSize;
            float gx = (x - originX) / Rac1NovalisPoolWater.CellSize;
            int r0 = Math.Max(0, (int)(gy - radius)), r1 = Math.Min(Cells - 1, (int)(gy + radius));
            int c0 = Math.Max(0, (int)(gx - radius)), c1 = Math.Min(Cells - 1, (int)(gx + radius));
            var grid = _heights[i][_source];
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                {
                    float dx = x - (originX + c * Rac1NovalisPoolWater.CellSize);
                    float dy = y - (originY + r * Rac1NovalisPoolWater.CellSize);
                    float d2 = dx * dx + dy * dy;
                    float v = d2 < 1f ? amount : amount / MathF.Sqrt(d2);
                    int cell = r * Cells + c;
                    if (add) v += _blendNext * _heights[i][_next][cell] + _blendSource * _heights[i][_source][cell];
                    else if (MathF.Abs(v) < MathF.Abs(grid[cell])) v = grid[cell];
                    grid[cell] = v;
                }
        }
    }

    /// <summary>The first update's <c>pool_ripple</c> calls: two radius-2 bumps of 0.2 near every tile centre.</summary>
    public void SeedInitialRipples(Func<float, float, float> randomBetween)
    {
        for (int i = 0; i < _tiles.Count; i++)
            for (int k = 0; k < 2; k++)
            {
                float x = _tiles[i].X + randomBetween(-6f, 6f);
                float y = _tiles[i].Y + randomBetween(-6f, 6f);
                Splash(x, y, 2f, 0.2f, add: false);
            }
    }
}
