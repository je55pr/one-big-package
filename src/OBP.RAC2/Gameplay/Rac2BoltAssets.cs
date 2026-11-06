using OBP.IO;
using OBP.PS2.Iso;
using OBP.PS2.Presentation;
using OBP.RAC2.Level;
using OBP.Runtime;

namespace OBP.RAC2.Gameplay;

/// <summary>The active level's original denomination models and texture remaps.</summary>
public static class Rac2BoltAssets
{
    public sealed record Model(int NativeClassId, IReadOnlyList<RuntimeObjectMesh> Surfaces);
    public sealed record Catalogue(IReadOnlyDictionary<int, Model> Models, IReadOnlyList<RuntimeTexture> Textures);

    public static int ClassForDenomination(int value) => value switch
    {
        1 => 13,
        5 => 14,
        20 => 15,
        50 => 16,
        100 => 17,
        500 => 18,
        1000 => 19,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    // Oozla constructor 0x378270 loads the seven 0.5 factors from
    // 0x1AEBB0..0x1AEBC8. The mesh decoder already applies class +0x24.
    public const float VisualScale = .5f;

    public static Catalogue Load(string path, int level)
    {
        using var reader = new FileRandomAccessReader(path);
        return Read(reader, level);
    }

    public static Catalogue Read(IRandomAccessReader reader, int level)
    {
        var wad = Iso9660Filesystem.Open(reader).OpenFile($"/G/LEVEL{level}.WAD")
            ?? throw new InvalidDataException("The R&C2 pickup source level is unavailable.");
        var header = Rac2LevelWad.ReadHeader(wad);
        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        var classes = Rac2MobyClasses.Read(core, level);
        var models = new Dictionary<int, Model>();
        var usedTextures = new HashSet<int>();
        foreach (int id in Enumerable.Range(13, 7))
        {
            if (!classes.TryGetValue(id, out var cls) || cls.Mesh.Indices.Length == 0)
                throw new InvalidDataException($"R&C2 Bolt model {id} is unavailable.");
            var mesh = cls.Mesh;
            var positions = new double[mesh.Positions.Length];
            for (int at = 0; at < positions.Length; at += 3)
            {
                positions[at] = mesh.Positions[at];
                positions[at + 1] = mesh.Positions[at + 2];
                positions[at + 2] = mesh.Positions[at + 1];
            }
            var surfaces = Enumerable.Range(0, mesh.Indices.Length / 3)
                .GroupBy(face => (Texture: cls.TriangleTextureIds[face], Material: mesh.TriangleMaterialStateIndices[face]))
                .Select(group =>
                {
                    usedTextures.Add(group.Key.Texture);
                    return new RuntimeObjectMesh("moby", group.Key.Texture, positions, mesh.Uvs,
                        group.SelectMany(face => mesh.Indices.AsSpan(face * 3, 3).ToArray()).ToArray(),
                        MaterialPresentation: group.Key.Material >= 0
                            ? Ps2MaterialPresentation.From(mesh.Materials[group.Key.Material], classifyMobySurface: true) : null);
                }).ToArray();
            models.Add(id, new(id, surfaces));
        }
        var textures = Rac2LevelTextures.Read(core, Rac2LevelTextures.Table.Moby)
            .Where(texture => usedTextures.Contains(texture.Index))
            .Select(texture => new RuntimeTexture("moby", texture.Index, texture.Width, texture.Height, texture.Rgba)).ToArray();
        if (usedTextures.Any(id => id >= 0 && !textures.Any(texture => texture.TextureId == id)))
            throw new InvalidDataException("An original R&C2 Bolt texture is unavailable.");
        return new(models, textures);
    }
}
