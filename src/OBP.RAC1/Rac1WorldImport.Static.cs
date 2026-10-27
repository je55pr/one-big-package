using OBP.PS2.Graphics;
using OBP.PS2.Presentation;
using OBP.PS2.Textures;
using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OBP.RAC1;

public static partial class Rac1WorldImport
{
    private static IReadOnlyList<RuntimeDynamicObject> AddPlacedStaticGeometry(
        Rac1LevelCore.Core core,
        byte[] gameplayBytes,
        int levelId,
        List<RuntimeMesh> meshes,
        List<RuntimeTexture> textures,
        List<RuntimeAnimatedMesh> animatedMeshes,
        ref double minX,
        ref double minY,
        ref double minZ,
        ref double maxX,
        ref double maxY,
        ref double maxZ)
    {
        var classes = Rac1StaticClasses.Read(core);
        var instances = Rac1Instances.Parse(gameplayBytes);

        var tieTextures = ReadRuntimeTextures(core, "tie", core.Header.TieTextures);
        var shrubTextures = ReadRuntimeTextures(core, "shrub", core.Header.ShrubTextures);
        var mobyTextures = ReadRuntimeTextures(core, "moby", core.Header.MobyTextures);
        textures.AddRange(tieTextures);
        textures.AddRange(shrubTextures);
        textures.AddRange(mobyTextures);

        var tieTextureIds = tieTextures.Select(t => t.TextureId).ToHashSet();
        var shrubTextureIds = shrubTextures.Select(t => t.TextureId).ToHashSet();
        var shrubAlphaProfiles = shrubTextures.ToDictionary(
            t => t.TextureId, t => AlphaProfile.Analyse(t.Rgba));
        var mobyTextureIds = mobyTextures.Select(t => t.TextureId).ToHashSet();

        animatedMeshes.AddRange(BuildAnimatedMobyMeshes(
            core, instances.MobyInstances, classes.Mobies, mobyTextureIds,
            out var animatedMobyInstances,
            ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ));

        PlaceMatrixInstances(
            "tie",
            instances.TieInstances.Select(i => (i.OClass, i.Matrix)),
            oClass => classes.Ties.TryGetValue(oClass, out var c)
                ? (c.Mesh.Positions, c.Mesh.Uvs, c.Mesh.Indices, c.TriangleTextureIds,
                    PresentationsFor(
                        c.Mesh.Materials, c.Mesh.TriangleMaterialSlots, null,
                        c.TriangleTextureIds.Length))
                : null,
            tieTextureIds,
            null,
            null,
            meshes,
            ref minX, ref minY, ref minZ,
            ref maxX, ref maxY, ref maxZ);

        PlaceMatrixInstances(
            "shrub",
            instances.ShrubInstances.Select(i => (i.OClass, i.Matrix)),
            oClass => classes.Shrubs.TryGetValue(oClass, out var c)
                ? (c.Mesh.Positions, c.Mesh.Uvs, c.Mesh.Indices, c.TriangleTextureIds,
                    PresentationsFor(
                        c.Mesh.Materials, c.Mesh.TriangleMaterialStateIndices,
                        c.Mesh.TriangleAlphaBlendEnabled, c.TriangleTextureIds.Length))
                : null,
            shrubTextureIds,
            shrubAlphaProfiles,
            levelId == 1 ? new HashSet<int> { 116, 121 } : null,
            meshes,
            ref minX, ref minY, ref minZ,
            ref maxX, ref maxY, ref maxZ);

        return BuildMobyDynamicObjects(
            levelId, instances.MobyInstances, classes.Mobies, mobyTextureIds, animatedMobyInstances,
            ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
    }

    private static List<RuntimeTexture> ReadRuntimeTextures(
        Rac1LevelCore.Core core,
        string kind,
        Rac1LevelCore.ArrayRange range)
    {
        var decoded = RcLevelTextureTable.Read(
            core.Index,
            core.Assets,
            core.GsRam,
            core.Header.TexturesBaseOffset,
            new RcLevelTextureTable.Range(range.Count, range.Offset));
        return decoded
            .Select(t => new RuntimeTexture(kind, t.Index, t.Width, t.Height, t.Rgba))
            .ToList();
    }

    private static RuntimeMaterialPresentation? PresentationFor(
        IReadOnlyList<RcMaterialState> materials,
        int[] stateIndices,
        bool?[]? alphaBlendEnabled,
        int face,
        bool classifyMobySurface = false)
    {
        if ((uint)face >= (uint)stateIndices.Length)
        {
            return null;
        }

        int stateIndex = stateIndices[face];
        if ((uint)stateIndex >= (uint)materials.Count)
        {
            return null;
        }

        bool? alphaBlend = alphaBlendEnabled is not null && (uint)face < (uint)alphaBlendEnabled.Length
            ? alphaBlendEnabled[face]
            : null;
        var presentation = Ps2MaterialPresentation.From(
            materials[stateIndex], alphaBlend, classifyMobySurface);
        return presentation.HasNativeEvidence ? presentation : null;
    }

    private static RuntimeMaterialPresentation?[] PresentationsFor(
        IReadOnlyList<RcMaterialState> materials,
        int[] stateIndices,
        bool?[]? alphaBlendEnabled,
        int faceCount)
    {
        var result = new RuntimeMaterialPresentation?[faceCount];
        for (int face = 0; face < faceCount; face++)
        {
            result[face] = PresentationFor(materials, stateIndices, alphaBlendEnabled, face);
        }

        return result;
    }

    private static void PlaceMatrixInstances(
        string kind,
        IEnumerable<(int OClass, float[] Matrix)> instances,
        Func<int, (double[] Positions, float[] Uvs, int[] Indices, int[] TriangleTextureIds, RuntimeMaterialPresentation?[] TrianglePresentations)?> lookup,
        HashSet<int> textureIds,
        IReadOnlyDictionary<int, AlphaProfile>? alphaProfiles,
        IReadOnlySet<int>? cutoutClasses,
        List<RuntimeMesh> meshes,
        ref double minX,
        ref double minY,
        ref double minZ,
        ref double maxX,
        ref double maxY,
        ref double maxZ)
    {
        var bySurface = new Dictionary<(int TextureId, RuntimeMaterialPresentation? Presentation, int SortGroup), (
            List<double> Positions,
            List<float> Uvs,
            List<int> Indices,
            Dictionary<(double X, double Y, double Z, double S, double T), int> Weld)>();

        int instanceOrdinal = 0;
        foreach (var instance in instances)
        {
            int sortInstance = ++instanceOrdinal;
            var cls = lookup(instance.OClass)
                ?? throw new InvalidDataException($"R&C1 {kind} placement references missing class {instance.OClass}.");
            if (cls.Indices.Length != cls.TriangleTextureIds.Length * 3)
            {
                throw new InvalidDataException($"R&C1 {kind} class {instance.OClass} triangle/material arrays disagree.");
            }
            if (cls.Uvs.Length != cls.Positions.Length / 3 * 2)
            {
                throw new InvalidDataException($"R&C1 {kind} class {instance.OClass} UV count is invalid.");
            }

            var worldPositions = new double[cls.Positions.Length];
            for (int i = 0; i < cls.Positions.Length; i += 3)
            {
                var (nx, ny, nz) = Rac1Instances.TransformPoint(
                    instance.Matrix,
                    cls.Positions[i],
                    cls.Positions[i + 1],
                    cls.Positions[i + 2]);
                // Native shrub decals can sit just 0.0025 world units above terrain.
                // Centimetre rounding collapses that authored separation and causes
                // exact coplanar depth fighting. Keep sub-centimetre shrub height;
                // retain the existing TIE weld/placement policy unchanged.
                double x = R2(nx), y = kind == "shrub" ? R4(nz) : R2(nz), z = R2(ny);
                worldPositions[i] = x;
                worldPositions[i + 1] = y;
                worldPositions[i + 2] = z;
                minX = System.Math.Min(minX, nx);
                minY = System.Math.Min(minY, nz);
                minZ = System.Math.Min(minZ, ny);
                maxX = System.Math.Max(maxX, nx);
                maxY = System.Math.Max(maxY, nz);
                maxZ = System.Math.Max(maxZ, ny);
            }

            for (int face = 0; face < cls.TriangleTextureIds.Length; face++)
            {
                int textureId = cls.TriangleTextureIds[face];
                if (textureId < 0 || !textureIds.Contains(textureId))
                {
                    throw new InvalidDataException($"R&C1 {kind} class {instance.OClass} references missing texture {textureId}.");
                }
                var presentation = (uint)face < (uint)cls.TrianglePresentations.Length
                    ? cls.TrianglePresentations[face]
                    : null;
                // Only the R&C1 importer opts its shrub surfaces into the
                // per-placement transparency policy; the neutral Godot host
                // must not branch on source-game identity.
                if (alphaProfiles is not null)
                {
                    // The saved swimming-area view identifies class 121 as the
                    // layered canopy and class 116 as the red-leaf shrub.
                    // Their authored leaf textures mix hard cutouts with a
                    // smaller band of partial-alpha pixels (roughly 7-16%).
                    // Depth-writing cutouts intentionally trade that soft
                    // alpha away for stable intersecting leaf-card depth on
                    // these two visually verified classes only. Other shrubs
                    // retain their native alpha-blend interpretation.
                    bool cutout = cutoutClasses?.Contains(instance.OClass) == true &&
                        alphaProfiles.TryGetValue(textureId, out var profile) &&
                        profile.CutoutFraction >= 0.05 &&
                        presentation?.AlphaBlendEnabled is true &&
                        presentation.BlendEquation is not (
                            RuntimeBlendEquation.FixedAlpha or RuntimeBlendEquation.AdditiveSourceAlpha or
                            RuntimeBlendEquation.AdditiveFixedAlpha);
                    presentation = (presentation ?? new RuntimeMaterialPresentation()) with
                    {
                        PreferPerPlacementFoliageDepth = true,
                        PreferFoliageCutout = cutout,
                    };
                }
                // Alpha-blended foliage cards need their own per-placement
                // sorting origin. A whole-level welded material surface cannot
                // be sorted against itself or neighboring trees in Godot.
                int sortGroup = alphaProfiles is not null &&
                    alphaProfiles.TryGetValue(textureId, out var alpha) &&
                    MaterialModel.RequiresFoliageAlphaSorting(kind, alpha, presentation)
                    ? sortInstance : 0;
                var surface = (textureId, presentation, sortGroup);
                if (!bySurface.TryGetValue(surface, out var group))
                {
                    bySurface[surface] = group = (
                        [], [], [],
                        new Dictionary<(double, double, double, double, double), int>());
                }

                for (int corner = 0; corner < 3; corner++)
                {
                    int sourceVertex = cls.Indices[face * 3 + corner];
                    if (sourceVertex < 0 || sourceVertex * 3 + 2 >= worldPositions.Length)
                    {
                        throw new InvalidDataException($"R&C1 {kind} class {instance.OClass} has an invalid index {sourceVertex}.");
                    }
                    double x = worldPositions[sourceVertex * 3];
                    double y = worldPositions[sourceVertex * 3 + 1];
                    double z = worldPositions[sourceVertex * 3 + 2];
                    double s = R4(cls.Uvs[sourceVertex * 2]);
                    double t = R4(cls.Uvs[sourceVertex * 2 + 1]);
                    var key = (x, y, z, s, t);
                    if (!group.Weld.TryGetValue(key, out int runtimeVertex))
                    {
                        runtimeVertex = group.Positions.Count / 3;
                        group.Weld[key] = runtimeVertex;
                        group.Positions.Add(x);
                        group.Positions.Add(y);
                        group.Positions.Add(z);
                        group.Uvs.Add((float)s);
                        group.Uvs.Add((float)t);
                    }
                    group.Indices.Add(runtimeVertex);
                }
            }
        }

        foreach (var (surface, group) in bySurface
                     .OrderBy(kv => kv.Key.TextureId)
                     .ThenBy(kv => kv.Key.Presentation?.ToString(), StringComparer.Ordinal)
                     .ThenBy(kv => kv.Key.SortGroup))
        {
            meshes.Add(new RuntimeMesh(
                kind,
                surface.TextureId,
                group.Positions.ToArray(),
                group.Uvs.ToArray(),
                group.Indices.ToArray(),
                MaterialPresentation: surface.Presentation));
        }
    }
}
