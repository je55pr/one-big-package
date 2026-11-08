using System.Buffers.Binary;

namespace OBP.RAC1.Level;

/// <summary>
/// Novalis's dynamic pool surfaces (rock pools and cave tanks), read from the level's executable
/// overlay. The class-751 controller (update 0x2fd0e8, draw callback 0x2fd0c0) owns a table of
/// 0x1190-byte tile records at 0x1e34c0; each is a 16x16-cell height field drawn as a 17x17 vertex
/// grid. Which tiles are drawn depends on which of seven authored clip volumes holds the camera.
/// Nothing here is committed retail data; tables are read from the player's own disc at load.
/// </summary>
public sealed class Rac1NovalisPoolWater
{
    public const int NativeLevelId = 1;
    public const int TileTableAddress = 0x1e34c0;
    public const int TileStride = 0x1190;
    public const int TableTileCount = 21;      // records in the table; only the first UsedTileCount are populated
    public const int UsedTileCount = 18;
    public const int ZoneCount = 7;
    public const int PoolControllerClass = 751;

    // Constants of the controller's first update and the shared tile initialiser (0x2b7a48), which
    // are immediates in the original code rather than table data.
    public const int Cells = 16;                 // height cells per tile side
    public const float TileOrigin = -8f;         // pool_light +8 / +0xc: grid corner relative to tile centre
    public const float CellSize = 1f;            // pool_light +0x10 / +0x14
    public const float SwellRadius = 0.03f;      // tile +0x40 (0x3cf5c28f)
    public static readonly float ScrollPerFrameU = BitConverter.Int32BitsToSingle(unchecked((int)0xbad1b717));
    public static readonly float SwellStep0 = BitConverter.Int32BitsToSingle(0x3ca3d70a);
    public static readonly float SwellStep1 = BitConverter.Int32BitsToSingle(unchecked((int)0xbcfdf3b6));
    public const float SwellPhase0 = MathF.PI / 2f;    // tile +0x30 (0x3fc90fdb)
    public const float SwellPhase1 = -MathF.PI / 2f;   // tile +0x34 (0xbfc90fdb)

    /// <summary>One pool tile: authored placement and GS state plus the per-zone visibility mask.</summary>
    public sealed record Tile(
        int Index,
        float X, float Y, float Z,
        IReadOnlyList<byte> Neighbours,   // y-16, x-16, y+16, x+16, then the four diagonals; 0xff = none
        int Texture,                      // +0x14: second GS context (reflection mapped)
        int SecondTexture,                // +0x18: first GS context (scrolling pattern)
        byte Alpha2Fix,                   // +0x1c: ALPHA_2 FIX
        byte Alpha1Fix,                   // +0x1d: ALPHA_1 FIX
        ushort ZoneMask,                  // sub-tile bits drawn while the zone is active (4x4 sub-tiles of 4x4 cells)
        int Zone,
        int FlatEdges,                    // bit 0 row 0, 1 column 16, 2 row 16, 3 column 0: forced to the tile height
        float ScrollV);                   // tile +0x2c; the cave tank tiles do not scroll in v

    /// <summary>One camera zone: tiles drawn while the camera is inside its clip volume.</summary>
    public sealed record Zone(int Index, int FirstTile, int TileCount, int SplashOdds, int ClipVolume);

    public IReadOnlyList<Tile> Tiles { get; }
    public IReadOnlyList<Zone> Zones { get; }

    private Rac1NovalisPoolWater(IReadOnlyList<Tile> tiles, IReadOnlyList<Zone> zones)
    {
        Tiles = tiles;
        Zones = zones;
    }

    public static Rac1NovalisPoolWater Read(
        Rac1ExecutableOverlay overlay, IReadOnlyList<Rac1Instances.MobyInstance> instances)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(instances);
        var controller = instances.SingleOrDefault(instance => instance.OClass == PoolControllerClass)
            ?? throw new InvalidDataException("Novalis has no class-751 pool controller placement.");
        if (controller.PVar is not { Length: >= ZoneCount * 4 } pvar)
            throw new InvalidDataException("Novalis pool controller has no clip-volume PVar.");

        int I(int address) => BinaryPrimitives.ReadInt32LittleEndian(overlay.Read(address, 4));
        float F(int address) => BinaryPrimitives.ReadSingleLittleEndian(overlay.Read(address, 4));

        var zones = new Zone[ZoneCount];
        for (int z = 0; z < ZoneCount; z++)
        {
            zones[z] = new Zone(z, I(0x1fa5e8 + z * 4), I(0x1fa608 + z * 4), I(0x1fa628 + z * 4),
                BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(z * 4)));
            if (zones[z].FirstTile < 0 || zones[z].TileCount < 0 || zones[z].FirstTile + zones[z].TileCount > UsedTileCount ||
                zones[z].SplashOdds <= 0)
                throw new InvalidDataException($"Novalis pool zone {z} is outside its tile table.");
        }

        var tiles = new Tile[UsedTileCount];
        for (int i = 0; i < UsedTileCount; i++)
        {
            int at = TileTableAddress + i * TileStride;
            var zone = zones.Single(candidate => i >= candidate.FirstTile && i < candidate.FirstTile + candidate.TileCount);
            var tile = overlay.Read(at, 0x50);
            // Class-751's first update: tile 15's far edge is flat; the four tank tiles (13-16) do not scroll in v.
            bool tank = i is >= 13 and <= 16;
            tiles[i] = new Tile(i,
                F(at), F(at + 4), F(at + 8),
                tile.Slice(0xc, 8).ToArray(),
                I(at + 0x14), I(at + 0x18), tile[0x1c], tile[0x1d],
                (ushort)(I(0x1fa590 + i * 4) & 0xffff), zone.Index,
                i == 15 ? 8 : 0,
                tank ? 0f : ScrollPerFrameU);
            var t = tiles[i];
            if (!float.IsFinite(t.X) || !float.IsFinite(t.Y) || !float.IsFinite(t.Z) ||
                t.Neighbours.Any(n => n != 0xff && n >= UsedTileCount))
                throw new InvalidDataException($"Novalis pool tile {i} is not a valid record.");
        }
        return new Rac1NovalisPoolWater(tiles, zones);
    }
}
