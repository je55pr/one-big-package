using Godot;
using OBP.RAC2.Gameplay;
using OBP.RAC3.Level;
using OBP.Runtime;
using OBP.Runtime.Diagnostics;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;
using OBP.Runtime.Player;

namespace OneBigPackage;

/// <summary>Main-thread Godot resource ownership for verified cross-game weapons.</summary>
public sealed class FusionWeaponAssets : IDisposable
{
    private readonly Dictionary<(string, int), ImageTexture> _textures = [];
    private readonly Dictionary<(string, int, RuntimeMaterialPresentation?), StandardMaterial3D> _materials = [];
    private readonly FusionWeaponData _data;
    public IReadOnlyDictionary<SourceWeaponKey, string> Problems => _data.Problems;
    public Rac2LancerAssets.HeldAssets? Lancer => _data.Lancer;
    public UyaGlobalGadget.Source? Cannon => _data.Cannon;
    public IReadOnlyDictionary<SourceWeaponKey, PlayerEquipmentAnimationSource> AvatarAnimations => _data.AvatarAnimations;
    public IReadOnlyList<RuntimeObjectMesh> Projectile => _data.Projectile;
    public PlaytestWeaponConfiguration Configuration(bool isolated) => _data.Configuration(isolated);

    public FusionWeaponAssets(FusionWeaponData data)
    {
        _data = data;
        try
        {
            foreach (var texture in data.Textures) AddTexture(texture);
        }
        catch { Dispose(); throw; }
    }

    private void AddTexture(RuntimeTexture texture)
    {
        using var image = Image.CreateFromData(texture.Width, texture.Height, false, Image.Format.Rgba8, texture.Rgba);
        image.GenerateMipmaps();
        _textures.Add((texture.AssetKind, texture.TextureId), ImageTexture.CreateFromImage(image));
    }

    public Material MaterialFor(RuntimeObjectMesh surface)
    {
        var key = (surface.AssetKind, surface.TextureId, surface.MaterialPresentation);
        if (_materials.TryGetValue(key, out var material)) return material;
        if (!_textures.TryGetValue((surface.AssetKind, surface.TextureId), out var texture)) throw new InvalidDataException($"Fusion asset has no source texture: {key}.");
        var plan = OBP.Godot.WorldMaterialFactory.ResolveNativePresentation(surface.MaterialPresentation);
        material = new StandardMaterial3D
        {
            AlbedoTexture = texture,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            VertexColorUseAsAlbedo = true,
            TextureRepeat = plan.TextureRepeat ?? true,
            TextureFilter = plan.TextureFilter ?? BaseMaterial3D.TextureFilterEnum.Linear,
            BlendMode = plan.BlendMode,
            Transparency = plan.Transparency ?? BaseMaterial3D.TransparencyEnum.Disabled,
        };
        _materials.Add(key, material); return material;
    }

    public IReadOnlyList<FusionWeaponData.Voice> Audio(WeaponAudioIntent cue) => _data.Audio.GetValueOrDefault((cue.Weapon, cue.LocalSound)) ?? [];
    public bool Available(SourceWeaponKey weapon) => !Problems.ContainsKey(weapon);
    public void Dispose()
    {
        foreach (var material in _materials.Values) material.Dispose();
        foreach (var texture in _textures.Values) texture.Dispose();
        _materials.Clear(); _textures.Clear();
    }
}
