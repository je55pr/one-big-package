using Godot;
using OBP.Runtime;
using OBP.Runtime.Player;
using OBP.Core;
using OBP.Fusion;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

public partial class OBPGame
{
    public IReadOnlyList<SourceWeaponKey?> ReadPlayerQuickSlots() => _playerNative?.Assignments.Capture().QuickSelect ?? [];
    private IReadOnlyDictionary<ObpSourceGame, string> PlayerEquipmentSources => Sources.Setup.Library.Attached.Values
        .ToDictionary(source => source.Game, source => source.Identity.BuildId);

    /// <summary>Read model for the future all-trilogy loadout UI; no native IDs or layouts are needed by that UI.</summary>
    public IReadOnlyList<EquipmentCatalogEntry> ReadPlayerEquipment() => PlayerEquipmentRegistrations.Catalog.Read(PlayerEquipmentSources, ObservePlayerEquipment);

    private EquipmentLiveState ObservePlayerEquipment(EquipmentDefinition definition)
    {
        var key = definition.Item;
        bool owned = definition.Category == EquipmentCategory.Appearance || key is { } item &&
            (item.SourceGame == ObpSourceGame.Rac1 ? _rac1Weapons.OwnsNativeItem(item.NativeItemId) :
                _playerNative?.LoadoutPolicy == PlayerLoadoutPolicy.DevelopmentAvailable || _playerNative is null && _fusion is not null);
        if (definition.Category == EquipmentCategory.Appearance)
            return new(owned, _playerSession?.Capture().Configuration.Avatar == definition.Appearance, _avatarPresentation.Avatar?.Identity == definition.Appearance,
                _avatarPresentation.Avatar?.Identity == definition.Appearance ? EquipmentResourceStatus.Available : EquipmentResourceStatus.Unavailable,
                EquipmentResourceStatus.Unavailable);
        if (definition.Category == EquipmentCategory.Gadget)
        {
            bool enabled = _playerNative?.Assignments.Capture().EnabledGadgets.Contains(key!.Value) == true;
            string? reason = key!.Value.NativeItemId == (int)Rac1TraversalEquipmentId.Swingshot && _player?.HasRac1SwingshotTargets != true
                ? "No admitted source-native Swingshot target is present." :
                key.Value.NativeItemId is (int)Rac1TraversalEquipmentId.HydroPack or (int)Rac1TraversalEquipmentId.OxygenMask && _rac1NovalisWater is null
                ? "No admitted source-native water context is present." : null;
            return new(owned, enabled, enabled && reason is null, EquipmentResourceStatus.NotRequired,
                EquipmentResourceStatus.Unavailable, ContextAllowed: reason is null, ContextReason: reason);
        }
        var binding = _equipmentPresentation.Binding(key!.Value);
        bool model = PlayerEquipmentModelAvailable(key.Value) && binding?.Ready == true;
        string? context = binding is { Ready: false } ? binding.Reason : key.Value.SourceGame == ObpSourceGame.Rac1 && key != PlayerEquipmentRegistrations.Wrench && PlayerPresentationAssets is null
            ? "The selected source-native equipment effect bank is not prepared." : null;
        var status = _fusion?.Status(key.Value);
        var icon = key.Value.SourceGame == ObpSourceGame.Rac1 ? Rac1WeaponIconCatalog.Ordinary((Rac1WeaponId)key.Value.NativeItemId) : null;
        bool iconReady = icon is { } nativeIcon && Rac1RetailUiAssets.Sprite(nativeIcon.AnimationId, nativeIcon.Frame) is not null;
        return new(owned, CurrentWeaponKey == key, key == PlayerEquipmentRegistrations.Wrench ? _rac1HeldItem.Hand.WrenchHeld : _rac1HeldItem.Hand.IsHolding(key.Value),
            model ? EquipmentResourceStatus.Available : EquipmentResourceStatus.Unavailable,
            iconReady ? EquipmentResourceStatus.Available : EquipmentResourceStatus.Unavailable, status?.Capacity > 0 ? status.Ammo : null, status?.Capacity > 0 ? status.Capacity : null,
            ContextAllowed: context is null, ContextReason: context);
    }

    public EquipmentChangeResult SelectPlayerEquipment(string catalogId)
    {
        var entry = ReadPlayerEquipment().SingleOrDefault(entry => entry.Definition.Id == catalogId);
        if (entry is null) return new(false, "Unknown catalog entry.");
        if (!entry.Selectable) return new(false, entry.Reason);
        if (entry.Definition.Category == EquipmentCategory.Gadget) return SetPlayerGadgetEnabled(catalogId, true);
        if (entry.Definition.Appearance is { } avatar)
        {
            if (_playerSession?.Capture().Configuration.Avatar != avatar)
                return new(false, "This appearance requires an admitted controller/pose profile before it can be selected.");
            RecordCaptureCommand(new("equip", catalogId));
            return new(true);
        }
        SelectWeaponKey(entry.Definition.Item!.Value);
        if (CurrentWeaponKey == entry.Definition.Item) RecordCaptureCommand(new("equip", catalogId));
        return new(CurrentWeaponKey == entry.Definition.Item, entry.Reason);
    }

    public EquipmentChangeResult AssignPlayerQuickSelect(int slot, string? catalogId)
    {
        if (_playerNative is null) return new(false, "This explicit parity/diagnostic session has no OBP loadout.");
        var entry = catalogId is null ? null : ReadPlayerEquipment().SingleOrDefault(entry => entry.Definition.Id == catalogId);
        if (catalogId is not null && (entry is null || entry.Definition.Category != EquipmentCategory.Weapon || !entry.Selectable))
            return new(false, entry?.Reason ?? "Quick Select requires an implemented weapon.");
        _playerNative.Assignments.Assign(slot, entry?.Definition.Item);
        RecordCaptureCommand(new("quick-slot", catalogId ?? "", slot));
        return new(true);
    }

    public EquipmentChangeResult UnequipPlayerEquipment()
    {
        if (_playerNative is null) return new(false, "This session has no OBP loadout.");
        _playerNative.Held.HoldWrench();
        _rac1EquipmentUseTicks = 0; _rac1ArsenalHeld = _rac1PyroEmitting = false;
        _fusionPoseTriggerHeld = false;
        ResetRac1WeaponAudio(); StopFusionAudio();
        RefreshRac1WrenchPresentationVisibility();
        RefreshRac1HudState();
        RecordCaptureCommand(new("unequip", ""));
        return new(true, "Wrench held. Your selected weapon and ammunition are retained.");
    }

    public EquipmentChangeResult SetPlayerGadgetEnabled(string catalogId, bool enabled)
    {
        var entry = ReadPlayerEquipment().SingleOrDefault(entry => entry.Definition.Id == catalogId);
        if (_playerNative is null || entry?.Definition.Category != EquipmentCategory.Gadget ||
            entry.Definition.Implementation != EquipmentImplementation.Implemented || enabled && !entry.Selectable)
            return new(false, entry?.Reason ?? "No supported gadget in this player loadout.");
        _playerNative.Assignments.Enable(entry.Definition, enabled, PlayerEquipmentRegistrations.Catalog);
        RefreshPlayerMovementEquipment();
        RecordCaptureCommand(new("gadget", catalogId, Enabled: enabled));
        return new(true, enabled ? entry.Reason : null);
    }

    public string? PlayerEquipmentConfigurationIdentity => _playerNative is null ? null :
        PlayerEquipmentRegistrations.Catalog.ConfigurationIdentity(_playerNative.LoadoutPolicy, PlayerEquipmentSources, _playerNative.Assignments.Capture(),
            _playerNative.Weapons.Selected, _playerSession?.Capture().Configuration.Avatar);

    private IReadOnlyList<SourceWeaponKey> PlayerWeaponOrder()
    {
        if (_playerNative is null) return FusionTestActive ? FusionWeaponSession.Weapons : FusionWeaponSession.DefaultQuickSelect(_rac1Weapons);
        return PlayerQuickSelectOrder().OfType<SourceWeaponKey>().ToArray();
    }

    private IReadOnlyList<SourceWeaponKey?> PlayerQuickSelectOrder() => _playerNative!.Assignments.WithOverflow(ReadPlayerEquipment()
            .Where(entry => entry.Definition.Category == EquipmentCategory.Weapon && entry.Definition.Implementation == EquipmentImplementation.Implemented && entry.State.Owned)
            .Select(entry => entry.Definition.Item!.Value).Where(key => key != PlayerEquipmentRegistrations.Wrench));

    private void RefreshPlayerEquipmentAvailability()
    {
        if (_playerNative is null) return;
        var selected = ReadPlayerEquipment().Single(entry => entry.Definition.Item == _playerNative.Weapons.Selected);
        if (!selected.Usable) _playerNative.Held.HoldWrench(); // Remembered selection survives; draw only after explicit input/admission.
        RefreshPlayerMovementEquipment();
    }

    private void RefreshPlayerMovementEquipment()
    {
        if (_playerNative is null || _player is not { } player) return;
        bool Enabled(Rac1TraversalEquipmentId id) => _playerNative.GadgetEnabled(id) &&
            PlayerEquipmentSources.GetValueOrDefault(ObpSourceGame.Rac1) == OBP.RAC1.Rac1Authority.Primary.BuildId;
        player.Rac1MovementEquipment = new(Enabled(Rac1TraversalEquipmentId.HeliPack), Enabled(Rac1TraversalEquipmentId.ThrusterPack),
            Enabled(Rac1TraversalEquipmentId.HydroPack), Enabled(Rac1TraversalEquipmentId.OxygenMask), Enabled(Rac1TraversalEquipmentId.Swingshot), false, false);
        player.Rac1ActiveBackpack = new[] { Rac1TraversalEquipmentId.HeliPack, Rac1TraversalEquipmentId.ThrusterPack, Rac1TraversalEquipmentId.HydroPack }
            .Cast<Rac1TraversalEquipmentId?>().FirstOrDefault(item => item.HasValue && Enabled(item.Value));
        player.Rac1ClankAvailable = player.Rac1ActiveBackpack.HasValue;
        player.Rac1SwingshotEquipped = Enabled(Rac1TraversalEquipmentId.Swingshot);
    }
    private Rac1PickupAssets.Catalogue? PlayerPresentationAssets =>
        OrdinaryPlayerSession ? _equipmentPresentation.SelectedAssets : _rac1PickupAssets;


    private bool PlayerEquipmentModelAvailable(SourceWeaponKey key) =>
        key == PlayerEquipmentRegistrations.Wrench ? _avatarPresentation.WrenchAsset is not null :
        key == FusionWeaponSession.BombGlove ? _avatarPresentation.GloveAsset is not null : key.SourceGame == ObpSourceGame.Rac1
            ? _rac1EquipmentAssets?.Models.ContainsKey(Rac1WeaponProfiles.Get((Rac1WeaponId)key.NativeItemId).ModelClass) == true
            : _fusionAssets?.Available(key) == true;


    private void BindPlayerEquipmentPresentation()
    {
        _equipmentPresentation.Bind(_avatarPresentation.Controller is null ? null : _avatarPresentation.Avatar,
            PlayerEquipmentSources, PlayerEquipmentModelAvailable, key =>
            {
                if (_avatarPresentation.Avatar?.Skeletal is not { } rig ||
                    _fusionAssets is not { } assets ||
                    !assets.AvatarAnimations.TryGetValue(key, out var animation))
                    return false;
                try
                {
                    new PlayerSkeletalSampler(rig.Skeleton).RequireCompatible(animation.Skeleton);
                    return true;
                }
                catch (InvalidDataException) { return false; }
            });
    }


    private void ConfigurePlayerEquipmentPresentation(RuntimeWorld world)
    {
        _equipmentPresentation.ConfigureWorld(world, PlayerPresentationAssets);
        _playerAudio ??= new(new GodotRac1PlayerAudioPlayback(this, () => _player, intent => _worldHost.PlayEffect(intent)),
            () => _avatarPresentation.Clock?.Tick ?? 0, () => ResetRac1WeaponAudio(),
            reason => GD.Print("[player-audio] " + reason));
        var source = Sources.Setup.Library.Get(ObpSourceGame.Rac1);
        _playerAudio.Bind(new(new(ObpSourceGame.Rac1, OBP.RAC1.Rac1Authority.Primary.BuildId, "player-audio"),
            OrdinaryPlayerSession ? 0 : world.LevelId, SourceFileStamp(source?.Path),
            WorldScenes.Current!.Plan.Request), source is null ? null : PlayerPresentationAssets, _args.FixedSeed,
            _args.Rac1WrenchSmoke || _args.Rac1WeaponsSmoke || _args.Rac1PlayerAudioSmoke, _args.Rac1PlayerAudioSmoke);
        // Foreign/unclassified contact material is deliberately treated as a
        // dry surface by the selected movement adapter. The authentic LEVEL0
        // footstep bank is an OBP fallback, not a recovered GC/UYA surface map.
    }

    private Node3D CreatePlayerEquipmentEffectNode(int nativeClass, string name) =>
        CreateRac1EffectNode(nativeClass, name, playerEquipment: true);

    private void PlayPlayerEquipmentSound(int nativeClass, int localSound, Vector3 position)
    {
        if (_playerAudio?.PlayEquipmentSound(nativeClass, localSound, new(-position.X, position.Y, position.Z)) == true)
            RecordRac1WeaponSound(nativeClass, localSound);
    }

    private Control? CreateEquipmentAppearancePreview(EquipmentCatalogEntry entry) =>
        _avatarPresentation.CreateAppearancePreview(entry);
}
