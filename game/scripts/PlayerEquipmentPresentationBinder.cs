using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;
using Godot;

namespace OBP.Game;

/// <summary>
/// Selected source-bank data lives beyond worlds. Effect meshes, material factories
/// and resolved hand sockets belong to one world/presentation binding only.
/// </summary>
internal sealed class PlayerEquipmentPresentationBinder
{
    internal Rac1ExplosionPresenter Explosions { get; } = new();
    internal Rac1EquipmentViewPresentation NativeViews { get; } = new();
    internal Rac1WeaponAudioPresenter WeaponAudio { get; } = new();
    private readonly Dictionary<SourceWeaponKey, EquipmentPresentationBinding> _bindings = [];
    private readonly Dictionary<int, List<(ArrayMesh Mesh, Material Material)>> _effectMeshes = [];
    private string? _selectedBankPath;
    private string? _preparedBankPath;
    private Rac1PickupAssets.Catalogue? _preparedBank;

    internal Rac1PickupAssets.Catalogue? SelectedAssets { get; private set; }
    internal WorldMaterialFactory? EffectMaterials { get; private set; }
    internal Dictionary<int, List<(ArrayMesh Mesh, Material Material)>> EffectMeshes => _effectMeshes;
    internal EquipmentPresentationBinding? Binding(SourceWeaponKey key) => _bindings.GetValueOrDefault(key);

    internal Rac1PickupAssets.Catalogue? Assets(bool ordinary, Rac1PickupAssets.Catalogue? parity) => ordinary ? SelectedAssets : parity;

    internal bool ModelAvailable(SourceWeaponKey key, PlayerAvatarPresentationHost avatar,
        Rac1EquipmentAssets.Catalogue? native, FusionWeaponAssets? shared) =>
        key == PlayerEquipmentRegistrations.Wrench ? avatar.WrenchAsset is not null :
        key == FusionWeaponSession.BombGlove ? avatar.GloveAsset is not null : key.SourceGame == ObpSourceGame.Rac1
            ? native?.Models.ContainsKey(Rac1WeaponProfiles.Get((Rac1WeaponId)key.NativeItemId).ModelClass) == true
            : shared?.Available(key) == true;

    internal void BindSelected(PlayerAvatarPresentationHost avatar, IReadOnlyDictionary<ObpSourceGame, string> sources,
        Rac1EquipmentAssets.Catalogue? native, FusionWeaponAssets? shared)
    {
        Bind(avatar.Controller is null ? null : avatar.Avatar, sources,
            key => ModelAvailable(key, avatar, native, shared), key =>
            {
                if (avatar.Avatar?.Skeletal is not { } rig || shared is null || !shared.AvatarAnimations.TryGetValue(key, out var animation)) return false;
                try
                {
                    new PlayerSkeletalSampler(rig.Skeleton).RequireCompatible(animation.Skeleton);
                    return true;
                }
                catch (InvalidDataException) { return false; }
            });
    }

    internal PlayerEquipmentPresentationState Observe(SourceWeaponKey key, bool modelAvailable, bool effectBankAvailable)
    {
        var binding = Binding(key);
        string? context = key.SourceGame == ObpSourceGame.Rac1 &&
            key != PlayerEquipmentRegistrations.Wrench && !effectBankAvailable
            ? "The selected source-native equipment effect bank is not prepared." : null;
        var icon = key.SourceGame == ObpSourceGame.Rac1 ? Rac1WeaponIconCatalog.Ordinary((Rac1WeaponId)key.NativeItemId) : null;
        bool iconReady = icon is { } nativeIcon && Rac1RetailUiAssets.Sprite(nativeIcon.AnimationId, nativeIcon.Frame) is not null;
        return new(modelAvailable ? EquipmentResourceStatus.Available : EquipmentResourceStatus.Unavailable,
            iconReady ? EquipmentResourceStatus.Available : EquipmentResourceStatus.Unavailable, context,
            BindingReady: binding?.Ready == true,
            BindingReason: binding?.Reason ?? (binding is null ? "Equipment presentation has not been bound to the selected player." : null));
    }

    internal void PrepareSelectedBank(string path, bool ordinary, bool sourceChanged)
    {
        if (!ordinary) return;
        if (_preparedBankPath == path && _preparedBank is not null) return;
        // Same-path ISO replacements may have a different stamp. Never borrow
        // the committed bank for a candidate whose source has changed.
        _preparedBank = !sourceChanged && SelectedAssets is not null && _selectedBankPath == path
            ? SelectedAssets : Rac1PickupAssets.Load(path, 0);
        _preparedBankPath = path;
    }

    internal void ApplyPreparedBank()
    {
        if (_preparedBank is null || _preparedBankPath is null)
            throw new InvalidOperationException("Selected player equipment assets were not prepared.");
        SelectedAssets = _preparedBank;
        _selectedBankPath = _preparedBankPath;
        DiscardPrepared();
    }

    internal void DiscardPrepared()
    {
        _preparedBank = null;
        _preparedBankPath = null;
    }

    internal void InvalidateSource()
    {
        RetireWorld();
        NativeViews.InvalidateSource();
        SelectedAssets = null;
        _selectedBankPath = null;
    }

    internal void ConfigureWorld(RuntimeWorld world, Rac1PickupAssets.Catalogue? assets)
    {
        RetireWorld();
        EffectMaterials = assets is null ? null : new(world with { Textures = assets.Textures });
    }

    internal void ClearBindings() => _bindings.Clear();

    internal void RetireWorld()
    {
        Explosions.RetireWorld();
        WeaponAudio.Reset(freeImmediately: true);
        NativeViews.RetireWorld();
        ClearBindings();
        _effectMeshes.Clear();
        EffectMaterials = null;
    }

    internal void Bind(PlayerAvatar? avatar, IReadOnlyDictionary<ObpSourceGame, string> sources,
        Func<SourceWeaponKey, bool> modelAvailable, Func<SourceWeaponKey, bool> foreignAnimationCompatible)
    {
        _bindings.Clear();
        if (avatar is null) return;
        var sockets = avatar.AnimationClips.SelectMany(clip => clip.AttachmentFrames?.Keys ?? [])
            .ToHashSet(StringComparer.Ordinal);
        foreach (var item in PlayerEquipmentRegistrations.Catalog.Definitions.Where(definition => definition.Category == EquipmentCategory.Weapon))
        {
            var key = item.Item!.Value;
            bool foreign = key.SourceGame != ObpSourceGame.Rac1;
            string socket = foreign ? PlayerEquipmentPose.HandAttachmentKey :
                key == PlayerEquipmentRegistrations.Wrench ? Rac1WrenchAttachment.SocketKey :
                key == FusionWeaponSession.BombGlove ? Rac1BombProjectileSession.GloveSocketKey :
                Rac1WeaponProfiles.HandSocket(Rac1WeaponProfiles.Get((Rac1WeaponId)key.NativeItemId).HandSemantic);
            bool compatible = avatar.Skeletal is not null;
            if (foreign)
            {
                compatible = avatar.Skeletal is not null && foreignAnimationCompatible(key);
                if (compatible) sockets.Add(socket);
            }
            string model = key == PlayerEquipmentRegistrations.Wrench ? "moby:71" :
                key == FusionWeaponSession.BombGlove ? "bomb-glove/held" :
                key.SourceGame == ObpSourceGame.Rac1 ? "moby:" + Rac1WeaponProfiles.Get((Rac1WeaponId)key.NativeItemId).ModelClass :
                key == FusionWeaponSession.Lancer ? "moby:" + OBP.RAC2.Gameplay.Rac2LancerProfile.HeldMobyClass :
                "moby:" + OBP.RAC3.Gameplay.Rac3ShockCannonProfile.HeldMobyClass;
            _bindings[key] = EquipmentPresentationBinding.Resolve(item, avatar.Identity, model, socket,
                sources, sockets, modelAvailable(key), compatible);
        }
    }
}
