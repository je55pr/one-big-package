using System.Buffers.Binary;
using System.Numerics;
using OBP.IO;
using OBP.PS2.Geometry;
using OBP.PS2.Iso;
using OBP.PS2.Presentation;
using OBP.RAC2.Level;
using OBP.Runtime;

namespace OBP.RAC2.Gameplay;

/// <summary>Original class-500 detachable meshes and their native corncob pivots, not fragments cut from the intact box.</summary>
public static class Rac2CrateAssets
{
    public sealed record Part(int Index, Vector3 Pivot, IReadOnlyList<RuntimeObjectMesh> Surfaces);
    public sealed record Catalogue(IReadOnlyDictionary<int, Part> Parts, IReadOnlyList<RuntimeTexture> Textures);
    public static Catalogue Load(string path, int level)
    {
        using var reader = new FileRandomAccessReader(path);
        var wad = Iso9660Filesystem.Open(reader).OpenFile($"/G/LEVEL{level}.WAD") ?? throw new InvalidDataException("Crate level is unavailable.");
        var header = Rac2LevelWad.ReadHeader(wad);
        return Read(Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0)));
    }
    public static Catalogue Read(Rac2LevelCore.Core core)
    {
        byte[] bytes = Rac2MobyClasses.ClassBytes(core, 500);
        if (bytes.Length < 0x48) throw new InvalidDataException("Truncated crate fragment class header.");
        int row = Enumerable.Range(0, core.Header.MobyClasses.Count).Single(index =>
            BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(core.Header.MobyClasses.Offset + index * 32 + 4)) == 500);
        int remap = core.Header.MobyClasses.Offset + row * 32 + 16;
        int corn = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(0x2e)) * 16;
        float scale = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(0x24)) / 1024;
        var parts = new Dictionary<int, Part>();
        var textures = new HashSet<int>();
        foreach (int part in new[] { 0, 2, 5 })
        {
            if (corn < 0x48 || corn > bytes.Length - 16 || bytes[corn + part + 1] == 255)
                throw new InvalidDataException("Crate fragment has no original pivot.");
            int pivotAt = corn + bytes[corn + part + 1] * 16;
            if (pivotAt > bytes.Length - 16) throw new InvalidDataException("Crate fragment pivot is out of bounds.");
            float F(int offset) => BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(pivotAt + offset)) * scale;
            Vector3 pivot = new(F(0), F(4), F(8));
            var mesh = GcUyaMoby.ReadBangle(bytes, part);
            var positions = new double[mesh.Positions.Length];
            for (int at = 0; at < positions.Length; at += 3)
            {
                positions[at] = mesh.Positions[at] - pivot.X;
                positions[at + 1] = mesh.Positions[at + 2] - pivot.Z;
                positions[at + 2] = mesh.Positions[at + 1] - pivot.Y;
            }
            var surfaces = Enumerable.Range(0, mesh.Indices.Length / 3)
                .GroupBy(face => (Slot: mesh.TriangleMaterialSlots[face], Material: mesh.TriangleMaterialStateIndices[face]))
                .Select(group =>
                {
                    if ((uint)group.Key.Slot >= 16) throw new InvalidDataException("Crate fragment texture slot is unsupported.");
                    int texture = core.Index[remap + group.Key.Slot];
                    textures.Add(texture);
                    return new RuntimeObjectMesh("moby", texture, positions, mesh.Uvs,
                        group.SelectMany(face => mesh.Indices.AsSpan(face * 3, 3).ToArray()).ToArray(),
                        MaterialPresentation: group.Key.Material >= 0 ? Ps2MaterialPresentation.From(mesh.Materials[group.Key.Material], classifyMobySurface: true) : null);
                }).ToArray();
            if (surfaces.Length == 0) throw new InvalidDataException("Original crate fragment is empty.");
            parts.Add(part, new(part, pivot, surfaces));
        }
        var decodedTextures = Rac2LevelTextures.Read(core, Rac2LevelTextures.Table.Moby).Where(texture => textures.Contains(texture.Index))
            .Select(texture => new RuntimeTexture("moby", texture.Index, texture.Width, texture.Height, texture.Rgba)).ToArray();
        if (textures.Any(id => id >= 0 && !decodedTextures.Any(texture => texture.TextureId == id)))
            throw new InvalidDataException("An original R&C2 crate fragment texture is unavailable.");
        return new(parts, decodedTextures);
    }
}
