using System.Security.Cryptography;
using OBP.IO;
using OBP.PS2.Presentation;
using OBP.PS2.Textures;
using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// The active level's retail bolt and crate-fragment classes. These are ordinary
/// class-directory models, including the exact level-specific texture remaps.
/// Every admitted class has a single static sequence0; movement rotates the Moby.
/// </summary>
public static class Rac1PickupAssets
{
    public sealed record Model(int NativeClassId, double Radius,
        IReadOnlyList<RuntimeObjectMesh> Surfaces, string SourceSha256, int VertexCount, int TriangleCount);
    public sealed record Catalogue(IReadOnlyDictionary<int, Model> Models, IReadOnlyList<RuntimeTexture> Textures,
        IReadOnlyDictionary<(int Class, int LocalSound), Rac1CrateAudio.Sound>? Sounds = null);

    public static Catalogue Load(string path, int levelId)
    {
        using var reader = new FileRandomAccessReader(path);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(level => level.LevelId == levelId);
        var core = Rac1LevelCore.Open(reader, level);
        return Decode(core) with { Sounds = Rac1CrateAudio.Decode(reader, level, core) };
    }

    internal static Catalogue Decode(Rac1LevelCore.Core core)
    {
        var classes = Rac1StaticClasses.Read(core);
        var models = new Dictionary<int, Model>();
        var textureIds = new HashSet<int>();
        foreach (var cls in classes.Mobies.Values.Where(cls => cls.OClass is >= 13 and <= 16 or 121 or 122 or 1192 or 348 or 349 or 354 or 355 or 357 or 358 or 1747 or 1748 or 1749 or 1933 or 1934 or 1935 or 1963 ||
                     Rac1CrateSupply.AmmoDefinitions.Any(ammo => ammo.ModelClass == cls.OClass)))
        {
            var mesh = cls.Mesh;
            if (mesh.Indices.Length == 0) continue;
            if (cls.Sequences.Count(slot => slot.Value is not null) != 1 || cls.Sequences[0].Value?.Frames.Count != 1)
                throw new InvalidDataException($"R&C1 effect class {cls.OClass} static sequence contract changed.");
            var surfaces = new List<RuntimeObjectMesh>();
            var groups = Enumerable.Range(0, mesh.Indices.Length / 3).GroupBy(face =>
            {
                int state = mesh.TriangleMaterialStateIndices[face];
                RuntimeMaterialPresentation? presentation = (uint)state < mesh.Materials.Count
                    ? Ps2MaterialPresentation.From(mesh.Materials[state], null, classifyMobySurface: true) : null;
                return (Texture: cls.TriangleTextureIds[face], Presentation: presentation);
            });
            foreach (var group in groups)
            {
                double[] positions = new double[mesh.Positions.Length];
                for (int at = 0; at < positions.Length; at += 3)
                {
                    positions[at] = mesh.Positions[at];
                    positions[at + 1] = mesh.Positions[at + 2];
                    positions[at + 2] = mesh.Positions[at + 1];
                }
                int[] indices = group.SelectMany(face => mesh.Indices.AsSpan(face * 3, 3).ToArray()).ToArray();
                if (group.Key.Texture >= 0) textureIds.Add(group.Key.Texture);
                surfaces.Add(new RuntimeObjectMesh("moby", group.Key.Texture, positions, mesh.Uvs, indices,
                    MaterialPresentation: group.Key.Presentation));
            }
            models.Add(cls.OClass, new(cls.OClass, mesh.BoundingRadius, surfaces,
                Convert.ToHexString(SHA256.HashData(core.Assets.AsSpan(cls.AssetOffset, cls.AssetLength))).ToLowerInvariant(),
                mesh.Positions.Length / 3, mesh.Indices.Length / 3));
        }
        foreach (int id in new[] { 13, 14, 15, 16 })
            if (!models.ContainsKey(id)) throw new InvalidDataException($"R&C1 level has no retail bolt class {id}.");
        var textures = RcLevelTextureTable.Read(core.Index, core.Assets, core.GsRam, core.Header.TexturesBaseOffset,
            new(core.Header.MobyTextures.Count, core.Header.MobyTextures.Offset))
            .Where(texture => textureIds.Contains(texture.Index))
            .Select(texture => new RuntimeTexture("moby", texture.Index, texture.Width, texture.Height, texture.Rgba)).ToArray();
        if (textureIds.Except(textures.Select(texture => texture.TextureId)).Any())
            throw new InvalidDataException("R&C1 effect model references an absent texture.");
        return new(models, textures);
    }
}
