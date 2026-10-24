using Godot;
using OBP.Core;
using OBP.IO;
using OBP.PS2;
using OBP.PS2.Audio;
using OBP.PS2.Iso;
using OBP.RAC2.Gameplay;
using OBP.RAC2.Level;
using OBP.RAC3.Gameplay;
using OBP.RAC3.Level;
using OBP.Runtime;
using OBP.Runtime.Audio;
using OBP.Runtime.Diagnostics;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OneBigPackage;

/// <summary>Local-only asset ownership for the opt-in fusion world.</summary>
public sealed class FusionWeaponAssets : IDisposable
{
    public sealed record Voice(double Delay, bool Loop, RuntimeAudioClip Clip, double Gain);
    private readonly Dictionary<(string, int), ImageTexture> _textures = [];
    private readonly Dictionary<(string, int, RuntimeMaterialPresentation?), StandardMaterial3D> _materials = [];
    private readonly Dictionary<(SourceWeaponKey, int), IReadOnlyList<Voice>> _audio = [];
    private readonly Dictionary<string, string> _identities = [];
    public Dictionary<SourceWeaponKey, string> Problems { get; } = [];
    public GcLancerAssets.HeldAssets? Lancer { get; private set; }
    public UyaGlobalGadget.Source? Cannon { get; private set; }
    public IReadOnlyList<RuntimeObjectMesh> Projectile { get; private set; } = [];
    private readonly string _loadoutId;
    public PlaytestWeaponConfiguration Configuration => new(_loadoutId, FusionDamagePolicy.Id,
        new SortedDictionary<string, string>(_identities, StringComparer.Ordinal));

    public FusionWeaponAssets(ObpSourceLibrary sources, bool isolated = true)
    {
        _loadoutId = isolated ? FusionWeaponSession.LoadoutId : FusionWeaponSession.DefaultLoadoutId;
        Load(sources.Get(ObpSourceGame.Rac1), FusionWeaponSession.BombGlove, _ => { });
        Load(sources.Get(ObpSourceGame.Rac2), FusionWeaponSession.Lancer, disc =>
        {
            var fs = Iso9660Filesystem.Open(disc);
            Lancer = GcLancerAssets.ReadHeld(fs.OpenFile(GcLancerAssets.GadgetIsoPath)!);
            AddTexture(GcLancerPresentation.HeldTexture(Lancer));
            var wad = fs.OpenFile("/G/LEVEL1.WAD")!;
            var core = GcLevelCore.Open(GcLevelWad.RequireLump(wad, GcLevelWad.ReadHeader(wad), 0));
            var projectile = GcMobyClasses.Read(core)[GcLancerProfile.ProjectileMobyClass];
            Projectile = GcLancerPresentation.ProjectileModel(projectile);
            foreach (var texture in GcLevelTextures.Read(core, GcLevelTextures.Table.Moby).Where(t => projectile.TextureIds.Contains(t.Index)))
                AddTexture(new(GcLancerPresentation.ProjectileAssetKind, texture.Index, texture.Width, texture.Height, texture.Rgba));
            for (int cue = 0; cue <= 1; cue++)
            {
                var voices = Ps2SoundRenderer989.TryRenderVoices(Lancer.SoundBank.Sounds[cue], Lancer.SoundBank.DecodeTone, seed: 0)
                    ?? throw new NotSupportedException($"Lancer sound {cue} needs an unsupported grain decoder.");
                _audio.Add((FusionWeaponSession.Lancer, cue), voices.Select(v => new Voice(
                    v.StartFrame / (double)Ps2SoundRenderer989.SampleRate, v.Loop,
                    new(Ps2SoundRenderer989.SampleRate, 1, v.Audio.Samples, v.Audio.LoopStartFrame, v.Audio.LoopEndFrame), .7)).ToArray());
            }
            // Exercise the resolved skin before admitting fire in the live host.
            _ = GcLancerPresentation.HeldModel(Lancer, GcLancerProfile.HeldIdleSequence, 0);
        });
        Load(sources.Get(ObpSourceGame.Rac3), FusionWeaponSession.ShockCannon, disc =>
        {
            Cannon = UyaGlobalGadget.Read(disc, UyaShockCannonProfile.HeldMobyClass);
            foreach (var texture in UyaShockCannonAssets.Textures(Cannon).Concat(
                UyaFxTextures.Read(UyaLevelCore.Open(disc, 1).Core, new HashSet<int> { 10, 23 }).Values)) AddTexture(texture);
            for (int cue = 0; cue < 6; cue++)
            {
                var sound = UyaShockCannonAssets.Audio(Cannon, cue, seed: 0);
                _audio.Add((FusionWeaponSession.ShockCannon, cue), sound.Voices.Select(v => new Voice(
                    v.StartSeconds, v.Loop, v.Clip, sound.HostGain(0))).ToArray());
            }
            _ = UyaShockCannonAssets.Model(Cannon, 0, 0);
        });
    }

    private void Load(ObpAttachedSource? source, SourceWeaponKey weapon, Action<FileRandomAccessReader> decode)
    {
        string title = weapon.SourceGame switch { "rac1" => "Ratchet & Clank", "rac2" => "Going Commando", _ => "Up Your Arsenal" };
        _identities[weapon.SourceGame] = "missing";
        if (source is null) { Problems[weapon] = $"Attach {title} NTSC-U in Game Sources."; return; }
        try
        {
            GD.Print($"[fusion] verifying {title} source before using its weapons");
            using var disc = new FileRandomAccessReader(source.Path);
            string hash = Hashing.Sha256Hex(disc);
            if (!string.Equals(hash, source.Identity.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Disc SHA-256 differs from the supported original NTSC-U build.");
            _identities[weapon.SourceGame] = source.Identity.BuildId + "/sha256:" + hash;
            decode(disc);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or ArgumentException)
        { Problems[weapon] = $"{title} weapon unavailable: {error.Message} Attach the supported NTSC-U source in Game Sources."; }
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

    public IReadOnlyList<Voice> Audio(WeaponAudioIntent cue) => _audio.GetValueOrDefault((cue.Weapon, cue.LocalSound)) ?? [];
    public bool Available(SourceWeaponKey weapon) => !Problems.ContainsKey(weapon);
    public void Dispose()
    {
        foreach (var material in _materials.Values) material.Dispose();
        foreach (var texture in _textures.Values) texture.Dispose();
        _materials.Clear(); _textures.Clear(); _audio.Clear();
    }
}
