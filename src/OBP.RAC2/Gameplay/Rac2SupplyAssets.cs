using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Elf;
using OBP.PS2.Iso;
using OBP.PS2.Presentation;
using OBP.RAC2.Level;
using OBP.Runtime;

namespace OBP.RAC2.Gameplay;

/// <summary>The active level's class-3291 mesh and mapped native ammunition descriptor.</summary>
public static class Rac2SupplyAssets
{
    public sealed record Ammo(int Index, int Amount, int Capacity);
    public sealed record Catalogue(Ammo Lancer, IReadOnlyList<RuntimeObjectMesh> Surfaces, IReadOnlyList<RuntimeTexture> Textures);

    public static Catalogue Load(string path, int level)
    {
        using var reader = new FileRandomAccessReader(path);
        var fs = Iso9660Filesystem.Open(reader);
        var wad = fs.OpenFile($"/G/LEVEL{level}.WAD") ?? throw new InvalidDataException("Supply source level is unavailable.");
        var header = Rac2LevelWad.ReadHeader(wad);
        var core = Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0));
        // Base Lancer descriptor addresses are recovered in Oozla's overlay.
        // Other overlays relocate their tables; presentation still uses this level.
        var descriptorWad = level == 1 ? wad : fs.OpenFile("/G/LEVEL1.WAD")
            ?? throw new InvalidDataException("Recovered base Lancer descriptor level is unavailable.");
        var descriptorHeader = level == 1 ? header : Rac2LevelWad.ReadHeader(descriptorWad);
        var overlay = Rac2LevelOverlay.Open(Rac2LevelWad.RequireLump(descriptorWad, descriptorHeader, 0));
        var elf = fs.OpenFile("/SCUS_972.68") ?? throw new InvalidDataException("R&C2 executable is unavailable.");
        var segments = Elf32Reader.ReadProgramHeaders(elf, Elf32Reader.ReadHeader(elf));
        // Mapping is resident core data; descriptors are replaced by the level overlay.
        byte mapped = Elf32Reader.ReadVirtualRange(elf, segments, 0x139568 + Rac2LancerProfile.WeaponIndex, 1)[0];
        var descriptor = overlay.ReadVirtual((uint)(0x2637a0 + mapped * 0xe0), 0xe0);
        int amount = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.AsSpan(0x8c));
        int capacity = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.AsSpan(0x8e));
        if (capacity != Rac2LancerProfile.AmmoCapacity || amount <= 0 || amount > capacity ||
            BinaryPrimitives.ReadInt32LittleEndian(descriptor.AsSpan(0x14)) != Rac2LancerProfile.HeldMobyClass)
            throw new InvalidDataException("R&C2 Lancer supply descriptor does not match its native identity.");
        var cls = Rac2MobyClasses.Read(core, level)[Rac2CrateSupply.AmmoPickupClass];
        var mesh = cls.Mesh;
        if (mesh.Indices.Length == 0) throw new InvalidDataException("Original indexed ammo pickup model is empty.");
        var positions = new double[mesh.Positions.Length];
        for (int at = 0; at < positions.Length; at += 3)
        {
            positions[at] = mesh.Positions[at];
            positions[at + 1] = mesh.Positions[at + 2];
            positions[at + 2] = mesh.Positions[at + 1];
        }
        var surfaces = Enumerable.Range(0, mesh.Indices.Length / 3)
            .GroupBy(face => (Texture: cls.TriangleTextureIds[face], Material: mesh.TriangleMaterialStateIndices[face]))
            .Select(group => new RuntimeObjectMesh("moby", group.Key.Texture, positions, mesh.Uvs,
                group.SelectMany(face => mesh.Indices.AsSpan(face * 3, 3).ToArray()).ToArray(),
                MaterialPresentation: group.Key.Material >= 0 ? Ps2MaterialPresentation.From(mesh.Materials[group.Key.Material], classifyMobySurface: true) : null)).ToArray();
        var used = surfaces.Select(surface => surface.TextureId).ToHashSet();
        var textures = Rac2LevelTextures.Read(core, Rac2LevelTextures.Table.Moby).Where(texture => used.Contains(texture.Index))
            .Select(texture => new RuntimeTexture("moby", texture.Index, texture.Width, texture.Height, texture.Rgba)).ToArray();
        if (used.Any(id => id >= 0 && !textures.Any(texture => texture.TextureId == id)))
            throw new InvalidDataException("Original R&C2 ammo texture is missing.");
        return new(new(Rac2LancerProfile.WeaponIndex, amount, capacity), surfaces, textures);
    }
}
