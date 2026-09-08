using OBP.Core.Math;
using OBP.IO;
using OBP.PS2.Collision;
using OBP.PS2.Geometry;
using OBP.RAC3.Geometry;
using OBP.RAC3.Level;
using OBP.Runtime;

namespace OBP.RAC3;

/// <summary>
/// Native UYA retail world reconstruction. Every admitted binary family has a
/// corresponding retail compatibility proof; unresolved Moby class geometry is
/// kept out while authored instance identity/PVars cross the runtime boundary.
/// </summary>
public static class Rac3WorldImport
{
    public sealed record ImportResult(RuntimeWorld World, int TfragCount, int TieInstanceCount, int ShrubInstanceCount,
        int MobyInstanceCount, int MobiesWithPvar, int SkyShellCount);

    public static RuntimeWorld Build(IRandomAccessReader disc, int tableIndex) => BuildObserved(disc, tableIndex).World;

    public static ImportResult BuildObserved(IRandomAccessReader disc, int tableIndex)
    {
        var opened = UyaLevelCore.Open(disc, tableIndex);
        var core = opened.Core;
        var gameplay = UyaGameplay.Read(opened.GameplayReader);
        var settings = UyaLevelSettings.Parse(gameplay.RawDecoded);
        var meshes = new List<RuntimeMesh>();
        var textures = new List<RuntimeTexture>();
        var collision = new List<RuntimeCollisionBlob>();
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        void Grow(double x, double y, double z) { minX = System.Math.Min(minX, x); minY = System.Math.Min(minY, y); minZ = System.Math.Min(minZ, z); maxX = System.Math.Max(maxX, x); maxY = System.Math.Max(maxY, y); maxZ = System.Math.Max(maxZ, z); }

        var texByKind = new Dictionary<string, IReadOnlyList<OBP.PS2.Textures.RcLevelTextureTable.Texture>>
        {
            ["tfrag"] = UyaAssets.ReadTextures(core, UyaAssets.TextureTable.Tfrag),
            ["moby"] = UyaAssets.ReadTextures(core, UyaAssets.TextureTable.Moby),
            ["tie"] = UyaAssets.ReadTextures(core, UyaAssets.TextureTable.Tie),
            ["shrub"] = UyaAssets.ReadTextures(core, UyaAssets.TextureTable.Shrub),
        };
        foreach (var (kind, list) in texByKind)
            textures.AddRange(list.Select(t => new RuntimeTexture(kind, t.Index, t.Width, t.Height, t.Rgba)));

        var tfragRange = UyaLevelCore.SectionRange(core, core.Header.Tfrags, allowZero: true)
            ?? throw new InvalidDataException("UYA core has no bounded tfrag section.");
        var tfrag = RcTfrag.Read(core.Assets.AsSpan(tfragRange.Offset, tfragRange.Size).ToArray());
        ValidateTextureIds("tfrag", tfrag.TriangleTextureIds, texByKind["tfrag"].Count);
        meshes.AddRange(GroupTfrags(tfrag));
        Grow(tfrag.BoundsMin.X, tfrag.BoundsMin.Y, tfrag.BoundsMin.Z); Grow(tfrag.BoundsMax.X, tfrag.BoundsMax.Y, tfrag.BoundsMax.Z);

        var ties = UyaAssets.ReadTieClasses(core);
        var shrubs = UyaAssets.ReadShrubClasses(core);
        PlaceStatics("tie", gameplay.TieInstances, ties, texByKind["tie"].Count, meshes, Grow);
        PlaceStatics("shrub", gameplay.ShrubInstances, shrubs, texByKind["shrub"].Count, meshes, Grow);

        var collisionRange = UyaLevelCore.SectionRange(core, core.Header.Collision)
            ?? throw new InvalidDataException("UYA core has no bounded collision section.");
        var cm = RcCollision.Read(core.Assets.AsSpan(collisionRange.Offset, collisionRange.Size).ToArray());
        var cp = new double[cm.Positions.Length];
        for (int i = 0; i < cm.Positions.Length; i += 3) { cp[i] = cm.Positions[i]; cp[i + 1] = cm.Positions[i + 2]; cp[i + 2] = cm.Positions[i + 1]; Grow(cp[i], cp[i + 1], cp[i + 2]); }
        var ci = new int[cm.Triangles.Count * 3]; var ct = new int[cm.Triangles.Count];
        for (int i = 0; i < cm.Triangles.Count; i++) { var f = cm.Triangles[i]; ci[i * 3] = f.A; ci[i * 3 + 1] = f.C; ci[i * 3 + 2] = f.B; ct[i] = f.MaterialId; }
        collision.Add(new RuntimeCollisionBlob(cm.Octants.Count, cp, ci, ct));

        int skyShellCount = 0;
        var skyRange = UyaLevelCore.SectionRange(core, core.Header.Sky);
        if (skyRange is { } sr && sr.Size > 0)
        {
            var sky = UyaSky.Read(core.Assets.AsSpan(sr.Offset, sr.Size).ToArray());
            skyShellCount = sky.Shells.Count;
            textures.AddRange(sky.Textures.Select(t => new RuntimeTexture("sky", t.Index, t.Width, t.Height, t.Rgba)));
            AddSkyMeshes(sky, meshes, minX, minY, minZ, maxX, maxY, maxZ);
        }

        var dynamicObjects = gameplay.MobyInstances.Select(m =>
        {
            var payloads = new List<RuntimeOpaquePayload> { new("rac3-moby-instance-gc-layout-compat", m.RawInstance) };
            if (m.PvarData is { } pv) payloads.Add(new("rac3-pvar-gc-layout-compat", pv));
            return new RuntimeDynamicObject("rac3", m.OClass, m.Index, m.UidCompatibility, $"moby:{m.OClass}",
                $"table:{tableIndex}:moby:{m.Index}", new RuntimeObjectTransform(UyaGameplay.MobyTransform(m)), Array.Empty<RuntimeObjectMesh>(), payloads);
        }).ToArray();

        if (double.IsPositiveInfinity(minX)) { minX = minY = minZ = -1; maxX = maxY = maxZ = 1; }
        var bounds = new ObpBounds(new Vec3(minX, minY, minZ), new Vec3(maxX, maxY, maxZ));
        var environment = new RuntimeEnvironment(settings.DeathHeight, settings.IsSphericalWorld, settings.BackgroundColour, settings.FogColour,
            settings.FogNearDistance, settings.FogFarDistance, settings.FogNearIntensity, settings.FogFarIntensity);
        int materialCount = texByKind.Values.Sum(x => x.Count) + (skyShellCount > 0 ? textures.Count(t => t.AssetKind == "sky") + 1 : 0);
        // Shared GC/UYA/DL settings-layout compatibility: native Z-up ship/start transform -> OBP Y-up.
        // UYA retail corroborates the 0x5c structure and all-row value pattern; exact executable field-name provenance remains open.
        bool defaultShipTransform = settings.ShipPosition == (20f, 20f, 20f) && settings.ShipRotationZ == 0f;
        RuntimeSpawn? ship = defaultShipTransform
            ? null
            : new RuntimeSpawn(settings.ShipPosition.X, settings.ShipPosition.Z, settings.ShipPosition.Y, settings.ShipRotationZ);
        var world = new RuntimeWorld("rac3", Rac3Authority.Primary.BuildId, tableIndex, null, null, meshes, textures, materialCount, collision, bounds, environment, ship,
            Lighting: null, AnimatedMeshes: null, DynamicObjects: dynamicObjects);
        return new ImportResult(world, tfrag.TfragCount, gameplay.TieInstances.Count, gameplay.ShrubInstances.Count, gameplay.MobyInstances.Count,
            gameplay.MobyInstances.Count(m => m.PvarData is not null), skyShellCount);
    }

    private static IEnumerable<RuntimeMesh> GroupTfrags(RcTfrag.Mesh mesh)
    {
        foreach (var group in Enumerable.Range(0, mesh.TriangleTextureIds.Length).GroupBy(f => mesh.TriangleTextureIds[f]).OrderBy(g => g.Key))
        {
            var remap = new Dictionary<int, int>(); var p = new List<double>(); var u = new List<float>(); var c = new List<float>(); var ind = new List<int>();
            bool colours = mesh.Colors.Length == mesh.Positions.Length;
            foreach (int face in group)
                for (int k = 0; k < 3; k++)
                {
                    int v = mesh.Indices[face * 3 + k];
                    if (!remap.TryGetValue(v, out int nv))
                    {
                        nv = p.Count / 3; remap[v] = nv; p.Add(mesh.Positions[v * 3]); p.Add(mesh.Positions[v * 3 + 1]); p.Add(mesh.Positions[v * 3 + 2]); u.Add(mesh.Uvs[v * 2]); u.Add(mesh.Uvs[v * 2 + 1]);
                        if (colours) { c.Add(mesh.Colors[v * 3]); c.Add(mesh.Colors[v * 3 + 1]); c.Add(mesh.Colors[v * 3 + 2]); c.Add(1f); }
                    }
                    ind.Add(nv);
                }
            yield return new RuntimeMesh("tfrag", group.Key, p.ToArray(), u.ToArray(), ind.ToArray(), colours ? c.ToArray() : null);
        }
    }

    private static void PlaceStatics(string kind, IReadOnlyList<UyaGameplay.MatrixInstance> instances, IReadOnlyDictionary<int, UyaAssets.StaticClass> classes,
        int textureCount, List<RuntimeMesh> meshes, Action<double, double, double> grow)
    {
        var groups = new Dictionary<int, (List<double> P, List<float> U, List<int> I)>();
        foreach (var inst in instances)
        {
            if (!classes.TryGetValue(inst.OClass, out var cls)) throw new InvalidDataException($"UYA {kind} instance {inst.Index} references missing class {inst.OClass}.");
            ValidateTextureIds(kind, cls.TriangleTextureIds, textureCount);
            var world = new double[cls.Positions.Length];
            for (int v = 0; v < cls.Positions.Length; v += 3)
            {
                double x = inst.Matrix[0] * cls.Positions[v] + inst.Matrix[4] * cls.Positions[v + 1] + inst.Matrix[8] * cls.Positions[v + 2] + inst.Matrix[12];
                double y = inst.Matrix[1] * cls.Positions[v] + inst.Matrix[5] * cls.Positions[v + 1] + inst.Matrix[9] * cls.Positions[v + 2] + inst.Matrix[13];
                double z = inst.Matrix[2] * cls.Positions[v] + inst.Matrix[6] * cls.Positions[v + 1] + inst.Matrix[10] * cls.Positions[v + 2] + inst.Matrix[14];
                world[v] = x; world[v + 1] = z; world[v + 2] = y; grow(x, z, y);
            }
            var localRemaps = new Dictionary<int, Dictionary<int, int>>();
            for (int f = 0; f < cls.TriangleTextureIds.Length; f++)
            {
                int tex = cls.TriangleTextureIds[f];
                if (!groups.TryGetValue(tex, out var g)) groups[tex] = g = ([], [], []);
                if (!localRemaps.TryGetValue(tex, out var remap)) localRemaps[tex] = remap = [];
                for (int k = 0; k < 3; k++)
                {
                    int v = cls.Indices[f * 3 + k];
                    if (!remap.TryGetValue(v, out int nv)) { nv = g.P.Count / 3; remap[v] = nv; g.P.Add(world[v * 3]); g.P.Add(world[v * 3 + 1]); g.P.Add(world[v * 3 + 2]); g.U.Add(cls.Uvs[v * 2]); g.U.Add(cls.Uvs[v * 2 + 1]); }
                    g.I.Add(nv);
                }
            }
        }
        foreach (var (tex, g) in groups.OrderBy(k => k.Key)) meshes.Add(new RuntimeMesh(kind, tex, g.P.ToArray(), g.U.ToArray(), g.I.ToArray()));
    }

    private static void AddSkyMeshes(UyaSky.Sky sky, List<RuntimeMesh> meshes, double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        if (double.IsPositiveInfinity(minX)) return;
        double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2, cz = (minZ + maxZ) / 2;
        double radius = System.Math.Max(1, System.Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY) + (maxZ - minZ) * (maxZ - minZ)) / 2);
        double shellMax = sky.Shells.SelectMany(s => s.Positions).Select(System.Math.Abs).DefaultIfEmpty(0).Max();
        double scale = shellMax > 0 ? radius * 1.7 / shellMax : 1;
        foreach (var shell in sky.Shells)
            foreach (var group in Enumerable.Range(0, shell.TriangleTextureIds.Length).GroupBy(f => shell.TriangleTextureIds[f]).OrderBy(g => g.Key))
            {
                var remap = new Dictionary<int, int>(); var p = new List<double>(); var u = new List<float>(); var c = new List<float>(); var ind = new List<int>();
                foreach (int face in group)
                    for (int k = 0; k < 3; k++)
                    {
                        int v = shell.Indices[face * 3 + k];
                        if (!remap.TryGetValue(v, out int nv)) { nv = p.Count / 3; remap[v] = nv; p.Add(cx + shell.Positions[v * 3] * scale); p.Add(cy + shell.Positions[v * 3 + 2] * scale); p.Add(cz + shell.Positions[v * 3 + 1] * scale); u.Add(shell.Uvs[v * 2]); u.Add(shell.Uvs[v * 2 + 1]); c.Add(1); c.Add(1); c.Add(1); c.Add(shell.Alpha[v]); }
                        ind.Add(nv);
                    }
                meshes.Add(new RuntimeMesh("sky", group.Key, p.ToArray(), u.ToArray(), ind.ToArray(), c.ToArray()));
            }
    }

    private static void ValidateTextureIds(string kind, IEnumerable<int> ids, int count)
    {
        int bad = ids.FirstOrDefault(id => id < 0 || id >= count, -1);
        if (bad != -1) throw new InvalidDataException($"UYA {kind} geometry references texture {bad} outside 0..{count - 1}.");
    }
}
