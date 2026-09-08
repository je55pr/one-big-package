using OBP.Core.Math;
using OBP.IO;
using OBP.PS2.Collision;
using OBP.PS2.Geometry;
using OBP.PS2.Textures;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.RAC1;

/// <summary>
/// Evidence-backed native R&amp;C1 world assembly. The production path currently
/// carries retail tfrags, decoded textures, camera-centred sky shells, octree
/// collision and the R&amp;C1-specific 0x50-byte level-settings generation into
/// the neutral runtime. Placed Tie/Shrub/Moby geometry is promoted separately.
/// </summary>
public static partial class Rac1WorldImport
{
    /// <summary>Build one native R&amp;C1 level into the game-neutral runtime model.</summary>
    public static RuntimeWorld Build(IRandomAccessReader disc, int levelId)
    {
        var catalogue = Rac1DiscIndex.Read(disc);
        var level = catalogue.Levels.SingleOrDefault(l => l.LevelId == levelId)
            ?? throw new ArgumentOutOfRangeException(nameof(levelId), $"R&C1 native level {levelId} is not present in the disc index.");
        var core = Rac1LevelCore.Open(disc, level);

        var tfragBytes = core.Assets.AsSpan(
            core.Header.TfragsOffset,
            core.Header.TfragsEnd - core.Header.TfragsOffset).ToArray();
        var tfrags = RcTfrag.Read(tfragBytes);

        var decodedTextures = RcLevelTextureTable.Read(
            core.Index,
            core.Assets,
            core.GsRam,
            core.Header.TexturesBaseOffset,
            new RcLevelTextureTable.Range(core.Header.TfragTextures.Count, core.Header.TfragTextures.Offset));
        var textures = decodedTextures
            .Select(t => new RuntimeTexture("tfrag", t.Index, t.Width, t.Height, t.Rgba))
            .ToList();
        var textureIds = textures.Select(t => t.TextureId).ToHashSet();

        var meshes = ToTfragMeshes(tfrags, textureIds).ToList();

        var collisionBytes = core.Assets.AsSpan(
            core.Header.CollisionOffset,
            core.Header.CollisionEnd - core.Header.CollisionOffset).ToArray();
        var nativeCollision = RcCollision.Read(collisionBytes);
        var collision = ToRuntimeCollision(nativeCollision);

        // Tfrags are already decoded to OBP Y-up. Collision remains native Z-up
        // in the shared codec and crosses that boundary here.
        double minX = System.Math.Min(tfrags.BoundsMin.X, nativeCollision.Bounds.Min.X);
        double minY = System.Math.Min(tfrags.BoundsMin.Y, nativeCollision.Bounds.Min.Z);
        double minZ = System.Math.Min(tfrags.BoundsMin.Z, nativeCollision.Bounds.Min.Y);
        double maxX = System.Math.Max(tfrags.BoundsMax.X, nativeCollision.Bounds.Max.X);
        double maxY = System.Math.Max(tfrags.BoundsMax.Y, nativeCollision.Bounds.Max.Z);
        double maxZ = System.Math.Max(tfrags.BoundsMax.Z, nativeCollision.Bounds.Max.Y);

        double levelRadius = 0.5 * System.Math.Sqrt(
            (maxX - minX) * (maxX - minX) +
            (maxY - minY) * (maxY - minY) +
            (maxZ - minZ) * (maxZ - minZ));
        int skyExtraMaterials = AddSky(core, levelRadius, meshes, textures);

        // Outer native range 1 is the retail gameplay WAD. The 0x50 first-part
        // settings record is a distinct R&C1 generation (GC later grows it to
        // 0x5c). Fog distances use the same /1024 world-position scale as the
        // native geometry; RuntimeEnvironment always carries world units.
        var gameplay = Rac1LevelSettings.ReadGameplay(disc, level);
        var animatedMeshes = new List<RuntimeAnimatedMesh>();
        AddPlacedStaticGeometry(
            core, gameplay, meshes, textures, animatedMeshes,
            ref minX, ref minY, ref minZ,
            ref maxX, ref maxY, ref maxZ);
        var settings = Rac1LevelSettings.Parse(gameplay);
        const float FogDistanceScale = 1f / 1024f;
        var environment = new RuntimeEnvironment(
            DeathHeight: settings.DeathHeight,
            IsSphericalWorld: false,
            BackgroundColour: settings.BackgroundColour,
            FogColour: settings.FogColour,
            FogNearDistance: settings.FogNearDistance * FogDistanceScale,
            FogFarDistance: settings.FogFarDistance * FogDistanceScale,
            FogNearIntensity: settings.FogNearIntensity,
            FogFarIntensity: settings.FogFarIntensity);
        var ship = new RuntimeSpawn(
            settings.ShipPosition.X,
            settings.ShipPosition.Z,
            settings.ShipPosition.Y,
            settings.ShipRotationZ);

        return new RuntimeWorld(
            Game: "rac1",
            BuildId: Rac1Authority.Primary.BuildId,
            LevelId: levelId,
            PlanetName: null,
            LocationName: null,
            Meshes: meshes,
            Textures: textures,
            MaterialCount: textures.Count + skyExtraMaterials,
            CollisionMeshes: [collision],
            Bounds: new ObpBounds(new Vec3(minX, minY, minZ), new Vec3(maxX, maxY, maxZ)),
            Environment: environment,
            Ship: ship,
            AnimatedMeshes: animatedMeshes.Count > 0 ? animatedMeshes : null);
    }

    private static IEnumerable<RuntimeMesh> ToTfragMeshes(RcTfrag.Mesh mesh, HashSet<int> textureIds)
    {
        if (mesh.Indices.Length != mesh.TriangleTextureIds.Length * 3)
        {
            throw new InvalidDataException("R&C1 tfrag triangle/material arrays disagree.");
        }
        if (mesh.Uvs.Length != mesh.Positions.Length / 3 * 2)
        {
            throw new InvalidDataException("R&C1 tfrag UV count does not match its vertex count.");
        }
        bool haveColours = mesh.Colors.Length == mesh.Positions.Length;
        if (!haveColours)
        {
            throw new InvalidDataException("R&C1 tfrag baked-colour count does not match its vertex count.");
        }

        var byTexture = new SortedDictionary<int, List<int>>();
        for (int face = 0; face < mesh.TriangleTextureIds.Length; face++)
        {
            int textureId = mesh.TriangleTextureIds[face];
            if (textureId >= 0 && !textureIds.Contains(textureId))
            {
                throw new InvalidDataException($"R&C1 tfrag face references missing texture {textureId}.");
            }
            if (!byTexture.TryGetValue(textureId, out var triangles))
            {
                byTexture[textureId] = triangles = [];
            }
            triangles.Add(mesh.Indices[face * 3]);
            triangles.Add(mesh.Indices[face * 3 + 1]);
            triangles.Add(mesh.Indices[face * 3 + 2]);
        }

        foreach (var (textureId, sourceIndices) in byTexture)
        {
            var remap = new Dictionary<int, int>();
            var positions = new List<double>();
            var uvs = new List<float>();
            var colours = new List<float>();
            var indices = new List<int>(sourceIndices.Count);

            foreach (int sourceVertex in sourceIndices)
            {
                if ((uint)sourceVertex >= mesh.Positions.Length / 3)
                {
                    throw new InvalidDataException($"R&C1 tfrag index {sourceVertex} is outside the vertex array.");
                }
                if (!remap.TryGetValue(sourceVertex, out int runtimeVertex))
                {
                    runtimeVertex = positions.Count / 3;
                    remap[sourceVertex] = runtimeVertex;
                    positions.Add(mesh.Positions[sourceVertex * 3]);
                    positions.Add(mesh.Positions[sourceVertex * 3 + 1]);
                    positions.Add(mesh.Positions[sourceVertex * 3 + 2]);
                    uvs.Add(mesh.Uvs[sourceVertex * 2]);
                    uvs.Add(mesh.Uvs[sourceVertex * 2 + 1]);
                    colours.Add(mesh.Colors[sourceVertex * 3]);
                    colours.Add(mesh.Colors[sourceVertex * 3 + 1]);
                    colours.Add(mesh.Colors[sourceVertex * 3 + 2]);
                    colours.Add(1f);
                }
                indices.Add(runtimeVertex);
            }

            yield return new RuntimeMesh(
                "tfrag",
                textureId,
                positions.ToArray(),
                uvs.ToArray(),
                indices.ToArray(),
                colours.ToArray());
        }
    }

    private static RuntimeCollisionBlob ToRuntimeCollision(RcCollision.Mesh mesh)
    {
        var positions = new double[mesh.Positions.Length];
        for (int i = 0; i < mesh.Positions.Length; i += 3)
        {
            positions[i] = mesh.Positions[i];
            positions[i + 1] = mesh.Positions[i + 2];
            positions[i + 2] = mesh.Positions[i + 1];
        }

        var indices = new int[mesh.Triangles.Count * 3];
        var materialIds = new int[mesh.Triangles.Count];
        for (int face = 0; face < mesh.Triangles.Count; face++)
        {
            var triangle = mesh.Triangles[face];
            indices[face * 3] = triangle.A;
            indices[face * 3 + 1] = triangle.B;
            indices[face * 3 + 2] = triangle.C;
            materialIds[face] = triangle.MaterialId;
        }

        return new RuntimeCollisionBlob(mesh.Octants.Count, positions, indices, materialIds);
    }
}
