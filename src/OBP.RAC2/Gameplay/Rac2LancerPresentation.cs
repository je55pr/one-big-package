using System.Buffers.Binary;
using OBP.PS2.Geometry;
using OBP.PS2.Presentation;
using OBP.PS2.Textures;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OBP.RAC2.Gameplay;

/// <summary>Source-local decoded assets; attachment and playback remain host policy.</summary>
public static class Rac2LancerPresentation
{
    public const string HeldAssetKind = "rac2-global-gadget:1490";
    public const string ProjectileAssetKind = "rac2-oozla-lancer:3070";

    public static RuntimeTexture HeldTexture(Rac2LancerAssets.HeldAssets source)
    {
        byte[] pif = source.TexturePif;
        if (pif.Length < 0x20) throw new InvalidDataException("Lancer PIF8 header is truncated.");
        int width = BinaryPrimitives.ReadInt32LittleEndian(pif.AsSpan(8));
        int height = BinaryPrimitives.ReadInt32LittleEndian(pif.AsSpan(12));
        if (pif.Length < 0x20 + 1024 || width is <= 0 or > 1024 || height is <= 0 or > 1024 ||
            pif.Length < 0x20 + 1024 + width * height)
            throw new InvalidDataException("Lancer PIF8 texture is truncated.");
        uint[] palette = Enumerable.Range(0, 256)
            .Select(i => BinaryPrimitives.ReadUInt32LittleEndian(pif.AsSpan(0x20 + i * 4, 4))).ToArray();
        var decoded = Ps2Texture.DecodePaletted8(new(width, height, pif.AsMemory(0x20 + 1024, width * height), palette));
        return new(HeldAssetKind, 0, width, height, decoded.Rgba);
    }

    public static IReadOnlyList<RuntimeObjectMesh> HeldModel(Rac2LancerAssets.HeldAssets source, int sequence, int frame)
    {
        var clip = source.Model.Sequences.Single(s => s.Index == sequence);
        double[] native = GcUyaGadgetPose.Pose(source.Model.Mesh, source.Model.Joints, source.InverseBindMatrices, clip.Frames[frame]);
        return Project(source.Model, native, HeldAssetKind);
    }

    public static IReadOnlyList<RuntimeObjectMesh> ProjectileModel(GcUyaMoby.MobyClass source) =>
        source.OClass == Rac2LancerProfile.ProjectileMobyClass
            ? Project(source, source.Mesh.Positions, ProjectileAssetKind)
            : throw new ArgumentException("Expected original Lancer projectile class.", nameof(source));

    private static IReadOnlyList<RuntimeObjectMesh> Project(GcUyaMoby.MobyClass source, double[] native, string kind)
    {
        var positions = new double[native.Length];
        for (int i = 0; i < native.Length; i += 3)
        { positions[i] = native[i]; positions[i + 1] = native[i + 2]; positions[i + 2] = native[i + 1]; }
        var surfaces = new Dictionary<(int Texture, int Material), List<int>>();
        for (int face = 0; face < source.Mesh.Indices.Length / 3; face++)
        {
            var key = (source.TriangleTextureIds[face], source.Mesh.TriangleMaterialStateIndices[face]);
            if (!surfaces.TryGetValue(key, out var indices)) surfaces.Add(key, indices = []);
            indices.AddRange(source.Mesh.Indices.AsSpan(face * 3, 3).ToArray());
        }
        return surfaces.Select(s => new RuntimeObjectMesh(kind, s.Key.Texture, positions, source.Mesh.Uvs, s.Value.ToArray(),
            MaterialPresentation: s.Key.Material >= 0 ? Ps2MaterialPresentation.From(source.Mesh.Materials[s.Key.Material], classifyMobySurface: true) : null)).ToArray();
    }
}
