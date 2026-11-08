using Godot;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using System.Security.Cryptography;

namespace OBP.Game;

/// <summary>
/// OBP-authored visual bindings: ten R&C1 donors with the user's difference overlays.
/// Native meshes, UVs and texture remaps remain ISO-backed; no recovered atlas is distributed.
/// </summary>
internal sealed class AmmoPickupPresentation
{
    private sealed record Visual(AmmoPickupFamily Family, Rac1WeaponId Donor, string? Cleanup = null, string? OriginalHash = null);
    private static readonly Visual[] Visuals =
    [
        new(AmmoPickupFamily.BombGlove, Rac1WeaponId.FirstRanged, "bomb-glove", "db3b725a170a7e17f6bcdb359300457fb7dcdab0f9d507155084eb5d2d8960d6"),
        new(AmmoPickupFamily.Pyrocitor, Rac1WeaponId.Pyrocitor, "pyrocitor", "23bd2453004b9aff18059fbf829e8368c045c0428db8db5a943b7462c8350e65"),
        new(AmmoPickupFamily.Blaster, Rac1WeaponId.Blaster, "blaster", "3273856cb9a46acbe461e03efaa8081c0403d31bb5c4230638d88b52a042cc89"),
        new(AmmoPickupFamily.GloveOfDoom, Rac1WeaponId.GloveOfDoom, "glove-of-doom", "3666582bade196fc99a1e88c263e9edc2950fbf8786312e5a478fa1c89b48ebd"),
        new(AmmoPickupFamily.MineGlove, Rac1WeaponId.MineGlove, "mine-glove", "fd76041dca4f33b00bf48f479af08e6eaef1c90e9b2b26edec7b25d430ebd0e8"),
        new(AmmoPickupFamily.Devastator, Rac1WeaponId.Devastator, "devastator", "b088122c2529f06b49036167cb4bc34b85b2bc31c3c7decccb1eebd65de082a8"),
        new(AmmoPickupFamily.Visibomb, Rac1WeaponId.VisibombGun, "visibomb", "d9b301a38226753134ee25415ecd6bf83bd0632c4cd307c3700bc1120ebfc579"),
        new(AmmoPickupFamily.TeslaClaw, Rac1WeaponId.TeslaClaw),
        new(AmmoPickupFamily.Ryno, Rac1WeaponId.Ryno),
        new(AmmoPickupFamily.DroneDevice, Rac1WeaponId.DroneDevice, "drone-device", "66e5d888c624b4b24a1228f3a8510ab7de3a3c3550116fd63958128aec40ae6e"),
    ];

    private readonly Dictionary<AmmoPickupFamily, (ArrayMesh Mesh, Material Material)[]> _models = [];
    private readonly Dictionary<AmmoPickupFamily, float> _groundOffsets = [];

    internal AmmoPickupPresentation(RuntimeWorld world, Rac1PickupAssets.Catalogue assets)
    {
        var textures = assets.Textures.ToDictionary(texture => texture.TextureId);
        var donors = new Dictionary<AmmoPickupFamily, Rac1PickupAssets.Model>();
        foreach (var visual in Visuals)
        {
            int nativeClass = Rac1CrateSupply.AmmoDefinitions.Single(definition => definition.Item == (int)visual.Donor).ModelClass;
            if (!assets.Models.TryGetValue(nativeClass, out var model))
                throw new InvalidDataException($"Ammo family {visual.Family}: original class {nativeClass} is unavailable.");
            donors.Add(visual.Family, model);
            if (visual.Cleanup is null) continue;
            var used = model.Surfaces.Select(surface => surface.TextureId).Where(id => id >= 0).Distinct().ToArray();
            var original = used.Select(id => textures[id]).SingleOrDefault(texture =>
                Convert.ToHexString(SHA256.HashData(texture.Rgba)).Equals(visual.OriginalHash, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"Ammo family {visual.Family}: cleanup overlay does not match its source atlas.");
            string path = $"res://assets/pickups/ammo/cleanup/rac1/{visual.Cleanup}.png";
            var overlay = GD.Load<Texture2D>(path) ?? throw new InvalidDataException($"Ammo cleanup asset is unavailable: {path}");
            using var image = overlay.GetImage();
            if (image.GetWidth() != original.Width || image.GetHeight() != original.Height)
                throw new InvalidDataException($"Ammo family {visual.Family}: cleanup atlas dimensions differ.");
            image.Convert(Image.Format.Rgba8);
            byte[] patch = image.GetData(), rgba = (byte[])original.Rgba.Clone();
            for (int at = 0; at < rgba.Length; at += 4)
            {
                int alpha = patch[at + 3];
                for (int channel = 0; channel < 3; channel++)
                    rgba[at + channel] = (byte)((rgba[at + channel] * (255 - alpha) + patch[at + channel] * alpha + 127) / 255);
            }
            textures[original.TextureId] = original with { Rgba = rgba };
        }
        var materials = new WorldMaterialFactory(world with { Textures = textures.Values.ToArray() });
        foreach (var (family, model) in donors)
        {
            _groundOffsets.Add(family, Math.Max(0, -(float)model.Surfaces.SelectMany(surface =>
                Enumerable.Range(0, surface.Positions.Length / 3).Select(i => surface.Positions[i * 3 + 1])).Min()));
            _models.Add(family, model.Surfaces.Select(surface =>
            {
                var arrays = new global::Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = Enumerable.Range(0, surface.Positions.Length / 3)
                    .Select(i => RuntimeWorldScene.ToScene(surface.Positions[i * 3], surface.Positions[i * 3 + 1], surface.Positions[i * 3 + 2])).ToArray();
                arrays[(int)Mesh.ArrayType.TexUV] = Enumerable.Range(0, surface.Uvs.Length / 2)
                    .Select(i => new Vector2(surface.Uvs[i * 2], surface.Uvs[i * 2 + 1])).ToArray();
                int[] indices = (int[])surface.Indices.Clone();
                for (int at = 0; at < indices.Length; at += 3) (indices[at + 1], indices[at + 2]) = (indices[at + 2], indices[at + 1]);
                arrays[(int)Mesh.ArrayType.Index] = indices;
                var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                return (mesh, (Material)materials.Instanced("moby", surface.TextureId, false, surface.MaterialPresentation));
            }).ToArray());
        }
    }

    internal float GroundOffset(AmmoPickupFamily family) => _groundOffsets[family];

    internal Node3D Create(AmmoPickupFamily family, Node3D parent, string name)
    {
        var node = new Node3D { Name = name };
        foreach (var (mesh, material) in _models[family])
            node.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material });
        parent.AddChild(node);
        return node;
    }
}
