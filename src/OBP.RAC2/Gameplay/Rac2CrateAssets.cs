using System.Buffers.Binary;
using System.Numerics;
using OBP.IO;
using OBP.PS2.Geometry;
using OBP.PS2.Iso;
using OBP.PS2.Presentation;
using OBP.RAC2.Level;
using OBP.Runtime;

namespace OBP.RAC2.Gameplay;

/// <summary>Original crate-family detachable meshes, contact points and native corncob pivots.</summary>
public static class Rac2CrateAssets
{
    public sealed record Part(int Index, Vector3 Pivot, IReadOnlyList<RuntimeObjectMesh> Surfaces, IReadOnlyList<Vector3> ContactVertices);
    public sealed record Catalogue(IReadOnlyDictionary<int, Part> Parts, IReadOnlyList<RuntimeTexture> Textures,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, Part>> LitInstances);
    public static Catalogue Load(string path, int level, int nativeClass = 500)
    {
        using var reader = new FileRandomAccessReader(path);
        var wad = Iso9660Filesystem.Open(reader).OpenFile($"/G/LEVEL{level}.WAD") ?? throw new InvalidDataException("Crate level is unavailable.");
        var header = Rac2LevelWad.ReadHeader(wad);
        return Read(Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0)),
            Rac2Instances.Read(Rac2LevelWad.RequireLump(wad, header, 2)), nativeClass);
    }
    public static Catalogue Read(Rac2LevelCore.Core core, Rac2Instances.Gameplay gameplay, int nativeClass = 500)
    {
        if (!Rac2CrateSupply.IsCrate(nativeClass)) throw new ArgumentOutOfRangeException(nameof(nativeClass));
        byte[] bytes = Rac2MobyClasses.ClassBytes(core, nativeClass);
        if (bytes.Length < 0x48) throw new InvalidDataException("Truncated crate fragment class header.");
        int remap = Rac2MobyClasses.ClassRows(core).Single(row => row.NativeClass == nativeClass).Offset + 16;
        int corn = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(0x2e)) * 16;
        float scale = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(0x24)) / 1024;
        var parts = new Dictionary<int, Part>();
        var nativeMeshes = new Dictionary<int, GcUyaMoby.Mesh>();
        var textures = new HashSet<int>();
        int[] partIndices = nativeClass switch
        {
            Rac2ExplosiveCrateSession.NativeClass => [],
            Rac2CrateSupply.NanotechClass or Rac2CrateSupply.AdvancedNanotechClass => [0, 1, 2],
            _ => [0, 2, 5]
        };
        foreach (int part in partIndices)
        {
            if (corn < 0x48 || corn > bytes.Length - 16 || bytes[corn + part + 1] == 255)
                throw new InvalidDataException("Crate fragment has no original pivot.");
            int pivotAt = corn + bytes[corn + part + 1] * 16;
            if (pivotAt > bytes.Length - 16) throw new InvalidDataException("Crate fragment pivot is out of bounds.");
            float F(int offset) => BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(pivotAt + offset)) * scale;
            Vector3 pivot = new(F(0), F(4), F(8));
            // 0x30695C..0x3069A8: packed s16 XYZ points follow the kernel sphere.
            // The first point's fourth short carries their count.
            int pointsAt = pivotAt + 16;
            if (pointsAt > bytes.Length - 8) throw new InvalidDataException("Crate fragment contact points are truncated.");
            int pointCount = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(pointsAt + 6));
            if (pointCount < 3 || pointCount > 32 || pointsAt + pointCount * 8L > bytes.Length)
                throw new InvalidDataException("Crate fragment contact points are out of bounds.");
            var contacts = Enumerable.Range(0, pointCount).Select(index =>
            {
                float C(int component) => BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(pointsAt + index * 8 + component * 2)) * scale;
                return new Vector3(C(0), C(1), C(2)) - pivot;
            }).ToArray();
            var mesh = GcUyaMoby.ReadBangle(bytes, part);
            nativeMeshes.Add(part, mesh);
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
            parts.Add(part, new(part, pivot, surfaces, contacts));
        }
        var decodedTextures = Rac2LevelTextures.Read(core, Rac2LevelTextures.Table.Moby).Where(texture => textures.Contains(texture.Index))
            .Select(texture => new RuntimeTexture("moby", texture.Index, texture.Width, texture.Height, texture.Rgba)).ToArray();
        if (textures.Any(id => id >= 0 && !decodedTextures.Any(texture => texture.TextureId == id)))
            throw new InvalidDataException("An original R&C2 crate fragment texture is unavailable.");
        var litInstances = new Dictionary<int, IReadOnlyDictionary<int, Part>>();
        foreach (var instance in gameplay.MobyInstances.Where(instance => instance.OClass == nativeClass))
        {
            var matrix = Rac2Instances.MatrixFromPosRotScale(instance.Position, instance.Rotation, instance.Scale == 0 ? 1 : instance.Scale);
            litInstances.Add(instance.Index, parts.ToDictionary(entry => entry.Key, entry =>
            {
                var colors = Rac2WorldImport.ShadeMobyVertices(nativeMeshes[entry.Key], instance, matrix, gameplay.DirLights, gameplay.PointLights);
                return entry.Value with
                {
                    Surfaces = entry.Value.Surfaces.Select(surface => surface with
                    {
                        Colors = colors,
                        VertexColoursAreAuthored = false,
                    }).ToArray()
                };
            }));
        }
        return new(parts, decodedTextures, litInstances);
    }
}
