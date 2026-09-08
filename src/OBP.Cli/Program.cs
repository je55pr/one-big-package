using System.Globalization;
using System.Text.Json;
using OBP.IO;
using OBP.RAC2;

// obp-test-import — decode a Going Commando retail level straight from an ISO and
// print the deterministic world-assembly summary as JSON. The native equivalent
// of `reference-ts/tools/gc-world.mjs`, used as the Checkpoint D acceptance gate.
//
//   obp test-import <GC.iso> [--level N] [--json]

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.Error.WriteLine("usage: obp test-import <GC.iso> [--level N] [--json]");
    return 2;
}

int argi = 0;
if (args[argi] == "test-import")
{
    argi++;
}

string? isoPath = null;
int level = 1;
bool jsonOnly = false;
for (; argi < args.Length; argi++)
{
    switch (args[argi])
    {
        case "--level":
            level = int.Parse(args[++argi], CultureInfo.InvariantCulture);
            break;
        case "--json":
            jsonOnly = true;
            break;
        default:
            isoPath ??= args[argi];
            break;
    }
}

if (isoPath is null)
{
    Console.Error.WriteLine("error: no ISO path given");
    return 2;
}

if (!File.Exists(isoPath))
{
    Console.Error.WriteLine($"error: ISO not found: {isoPath}");
    return 1;
}

using var reader = new FileRandomAccessReader(isoPath);
var world = GcWorldImport.Build(reader, level);

var byKind = world.Meshes
    .GroupBy(m => m.AssetKind)
    .OrderBy(g => g.Key)
    .ToDictionary(g => g.Key, g => new
    {
        meshes = g.Count(),
        triangles = g.Sum(m => m.TriangleCount),
        texturedMeshes = g.Count(m => m.TextureId >= 0
            && world.Textures.Any(t => t.AssetKind == m.AssetKind && t.TextureId == m.TextureId)),
        missingTexIds = g.Where(m => m.TextureId >= 0
                && !world.Textures.Any(t => t.AssetKind == m.AssetKind && t.TextureId == m.TextureId))
            .Select(m => m.TextureId).Distinct().OrderBy(x => x).ToArray(),
    });

var texByKind = world.Textures
    .GroupBy(t => t.AssetKind)
    .OrderBy(g => g.Key)
    .ToDictionary(g => g.Key, g => new
    {
        count = g.Count(),
        bad = g.Count(t => t.Width <= 0 || t.Height <= 0 || t.Rgba.Length != t.Width * t.Height * 4),
        ids = g.Select(t => t.TextureId).OrderBy(x => x).ToArray(),
    });

var summary = new
{
    level = world.LevelId,
    planet = world.PlanetName,
    location = world.LocationName,
    renderMeshes = world.Meshes.Count,
    renderTriangles = world.TotalRenderTriangles,
    dynamicObjects = (world.DynamicObjects ?? Array.Empty<OBP.Runtime.RuntimeDynamicObject>())
        .GroupBy(o => o.NativeClassId)
        .OrderBy(g => g.Key)
        .ToDictionary(
            g => g.Key,
            g => new { instances = g.Count(), triangles = g.Sum(o => o.Meshes.Sum(m => m.TriangleCount)) }),
    dynamicTriangles = world.TotalDynamicTriangles,
    materials = world.MaterialCount,
    byKind,
    texByKind,
    collision = world.CollisionMeshes.Select(c => new { c.Octants, c.Triangles }).ToArray(),
    collisionTriangles = world.CollisionMeshes.Sum(c => c.Triangles),
    bounds = new
    {
        min = new[] { world.Bounds.Min.X, world.Bounds.Min.Y, world.Bounds.Min.Z },
        max = new[] { world.Bounds.Max.X, world.Bounds.Max.Y, world.Bounds.Max.Z },
    },
    environment = world.Environment is { } e
        ? new
        {
            deathHeight = e.DeathHeight,
            isSphericalWorld = e.IsSphericalWorld,
            fogColour = e.FogColour is { } f ? new[] { f.R, f.G, f.B } : null,
            fogNearDistance = e.FogNearDistance,
            fogFarDistance = e.FogFarDistance,
            fogNearIntensity = e.FogNearIntensity,
            fogFarIntensity = e.FogFarIntensity,
        }
        : null,
    shipSpawn = world.Ship is { } s ? new { pos = new[] { s.X, s.Y, s.Z }, yaw = s.Yaw } : null,
};

var opts = new JsonSerializerOptions { WriteIndented = true };
Console.WriteLine(JsonSerializer.Serialize(summary, opts));

if (!jsonOnly)
{
    Console.Error.WriteLine(
        $"LEVEL{level}: {world.Meshes.Count} render meshes / {world.TotalRenderTriangles} tris, " +
        $"{world.CollisionMeshes.Sum(c => c.Triangles)} collision tris, {world.MaterialCount} materials");
}

return 0;
