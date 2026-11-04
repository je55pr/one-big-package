using System.Text.Json;
using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Presentation;
using OBP.RAC1.Progression;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;
using OBP.Runtime.Presentation;

namespace OBP.Game;

/// <summary>
/// Owns the lazily built player-lifecycle, Fusion weapon, R&amp;C1 combat-input, profile and
/// actor-host services and binds them to the application's owned components. Damage, death,
/// weapon, profile and actor rules stay in those services; this module only supplies typed
/// observation and action ports.
/// </summary>
internal sealed class PlayerServicesComposition(ApplicationComponents components) : CompositionBase(components)
{
    private bool InWorldMode => Context.Mode().World;
    private (long Length, long Written)? _avatarSourceStamp => _persistentPlayer.AvatarSourceStamp;
    private WorldSceneLifetime<GodotWorldResources> WorldScenes => Context.WorldScenes();
    private SourceWeaponKey CurrentWeaponKey => Context.CurrentWeapon();
    private SourceWeaponKey? _previousSelectedWeapon { get => Modals.PreviousSelectedWeapon; set => Modals.PreviousSelectedWeapon = value; }
    private void RefreshPlayerSourceAvailability() => Context.RefreshPlayerSources();
    private void FlushRac1CampaignSaves(string boundary) => _rac1Campaign.Persistence.FlushAndReport(boundary);
    private void ClosePauseMenu() => Modals.ClosePause();

    private GodotPlayerLifecycleHost? _playerLife;
    internal GodotPlayerLifecycleHost Life => _playerLife ??= CreatePlayerLifecycle();
    private GodotPlayerLifecycleHost CreatePlayerLifecycle() => new(new(
        () => _playerNative, () => _playerSession, () => !_rac1Nanotech.Probe().IsDead,
        () => _activeDestination is { } destination ? OnDestinationChosen(destination) : EnterFusionLab(),
        () => _ui, () => _world?.Environment?.DeathHeight,
        node => node.Source.SourceGame == ObpSourceGame.Rac1
            ? Rac1World.Context.MobyRuntime.TryGet(new(node.Source.NativeClassId, node.Source.InstanceIndex), out var native) && native!.IsActive
            : _worldScenes?.Current?.Resources.Rac2Gameplay?.IsLiveHostile(node) == true,
        () => _worldScenes?.Current?.Resources.Rac1Gameplay?.Lifecycle.RetryPanel is not null,
        () => { FusionWeapons.ClearOnDeath(); Rac1WrenchHost.ResetOrFallback(_worldScenes?.Current?.Resources.Rac1Gameplay?.Wrench, _playerAudio, _player); },
        (before, after) => _playerAudio?.NotifyDamage(before, after), () => RefreshRac1HudState(), _hudState,
        () => Rac1HudProjection.Capture(_rac1Nanotech, _rac1Weapons, capturedMobyCount: _persistentPlayer.Rac1Use.Session?.Cargo.Count ?? 0,
            swimming: _player?.Rac1SwimState, ownsOxygenMask: _player?.Rac1MovementEquipment.OxygenMask == true),
        FusionWeapons.Hud));
    private FusionWeaponHost? _fusionWeapons;
    internal FusionWeaponHost Fusion => _fusionWeapons ??= CreateFusionWeaponHost();
    // Persistent player owns selection/input; the engine root only supplies deferred sources.
    internal IRac1WeaponPlayerInput CombatInput => _persistentPlayer.CombatInput ??=
        new Rac1WeaponPlayerInputHost(_persistentPlayer,
            new(() => _player, () => _rac1Weapons, () => _rac1HeldItem,
                () => _worldScenes?.Current?.Resources.Rac1Gameplay,
                () => PlayerLife.Alive, () => CurrentWeaponKey,
                key => _previousSelectedWeapon = key),
            new(() => FusionWeapons,
                () => _playerNative is null ||
                    ReadPlayerEquipment().Single(entry => entry.Definition.Item == _playerNative.Weapons.Selected).Usable,
                weapon =>
                {
                    if (_playerNative is null) return (true, (string?)null);
                    var entry = ReadPlayerEquipment().SingleOrDefault(entry =>
                        entry.Definition.Item == new SourceWeaponKey(ObpSourceGame.Rac1, (int)weapon));
                    return (entry?.Selectable == true, entry?.Reason);
                },
                () => QuickSelectOpen, message => MovementReproduction.Report(message)),
            new(previous => _equipmentPresentation.RefreshRac1HeldItem(previous, Rac1HandPresentation, _avatarPresentation),
                () => _equipmentPresentation.HoldSelectedRac1HandItem(Rac1HandPresentation, _avatarPresentation),
                () => RefreshRac1HudState(),
                () => _equipmentPresentation.WeaponAudio.Reset(),
                weapon => _equipmentPresentation.NativeViews.PlaySelection(weapon)));

    private FusionWeaponHost CreateFusionWeaponHost() => new(_host, new(
        Options: () => new(_playerNative?.Weapons, _captureReplay?.Recorder?.Reader is { Header.Weapons: null },
            _args.FusionTest, _args.FixedSeed ?? 1440, OrdinaryPlayerSession, _activeDestination?.DestinationId,
            _captureReplay?.Recorder is not null, Sources.Setup.Access == SourceAccessMode.OrdinaryPlay || _args.CaptureFrame is not null || _args.ShotsPath is not null),
        Snapshot: () => new(_player is not null, MovementReproduction.IsLoading, PlayerLife.Alive, _player?.Scripted == true,
            _player?.Rac1WrenchFrame.Active == true, (_worldScenes?.Current?.Resources.Rac1Gameplay?.Wrench.ThrowActive == true), _player?.Rac1WaterHandItemHidden == true,
            QuickSelectOpen, _player?.Rac1EquippedItemHeld == true),
        Inventory: () => _rac1Weapons, Hand: () => _rac1HeldItem.Hand, Equipment: ReadPlayerEquipment,
        SelectNative: Rac1CombatInput.Select,
        HoldNative: () => _equipmentPresentation.HoldSelectedRac1HandItem(Rac1HandPresentation, _avatarPresentation),
        SelectionChanged: previous =>
        {
            _previousSelectedWeapon = previous;
            Rac1WeaponUsePresentationHost.ForeignSelectionChanged(_worldScenes?.Current?.Resources.Rac1Gameplay,
                _persistentPlayer.Rac1Use, _equipmentPresentation);
        },
        RefreshAvailability: () => _persistentPlayer.Equipment.RefreshAvailability(),
        RefreshHeldVisibility: () => _equipmentPresentation.RefreshRac1HandVisibility(Rac1HandPresentation, _avatarPresentation), RefreshHud: () => RefreshRac1HudState(),
        ClearNativeTransients: _ => _worldScenes?.Current?.Resources.Rac1Gameplay?.ClearNativeWeaponTransients(_equipmentPresentation, () => _rac1Weapons),
        IsolateCampaign: start => _persistentPlayer.IsolateCampaign(_rac1Campaign, start),
        ReloadFixture: enabled =>
        {
            FlushRac1CampaignSaves("before fusion test");
            Context.ReplaceArgs(_args with { FusionTest = enabled });
            ClosePauseMenu();
            OnDestinationChosen(_activeDestination!);
        },
        Report: MovementReproduction.Report, Record: (kind, fact) => CaptureReplay.RecordGameplayFact(kind, fact)),
        new(() => Sources.Setup.Library.Attached.Values, RefreshPlayerSourceAvailability,
            () => ActorHost.InvalidateSources(), () => _equipmentPresentation.BindSelected(_avatarPresentation,
                _persistentPlayer.Equipment.Sources, _equipmentPresentation.NativeViews.Assets, FusionWeapons.Sources.Assets)),
        new(new(() => _player, () => Rac1WeaponUsePresentationHost.EquipmentForward(_player), () => OrdinaryPlayerSession,
            () => OrdinaryPlayerSession
                ? ActorHost.Combat.ReadGeometry().Select(target => new FusionWeaponTargetGeometry(target.Key, target.Bounds, true, false))
                : Rac1World.Weapons.Contacts.Geometry.Select(target => new FusionWeaponTargetGeometry(
                    new(ObpSourceGame.Rac1, target.Node.Source.NativeClassId, target.Node.Source.InstanceIndex), target.Bounds,
                    Rac1World.Enemies.IsRac1GameplayActive(target.Node), Rac1World.Enemies.Roster.TryGetValue(target.Node.Source.InstanceIndex, out var enemy) && enemy.RewardEmitted)),
            (query, muzzle) => ActorHost.Combat.Trace(query, muzzle), intent => FusionWeaponWorldActions.Apply(
                intent, OrdinaryPlayerSession, () => ActorHost, () => Rac1World,
                (damage, accepted) => CaptureReplay.RecordGameplayFact("damage", new { Intent = damage, Accepted = accepted })),
            () => Rac1World.Weapons.Contacts.RayExclusions(), (from, to) => Rac1World.Weapons.Contacts.HasLineOfSight(from, to))), () => _avatarPresentation.View,
        new((nativeClass, name) => Rac1World.Loot.CreateRac1EffectNode(nativeClass, name, playerEquipment: true),
            (nativeClass, localSound, position) =>
            {
                if (_playerAudio?.PlayEquipmentSound(nativeClass, localSound, new(-position.X, position.Y, position.Z)) == true)
                    _equipmentPresentation.WeaponAudio.Record(nativeClass, localSound, (_worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.WorldTick ?? 0), _args.Rac1WeaponsSmoke);
            }, world =>
            {
                var assets = _equipmentPresentation.Assets(OrdinaryPlayerSession, Rac1World.Loot.PickupAssets);
                _equipmentPresentation.ConfigureWorld(world, assets);
                var source = Sources.Setup.Library.Get(ObpSourceGame.Rac1);
                _playerAudio!.Bind(new(new(ObpSourceGame.Rac1, OBP.RAC1.Rac1Authority.Primary.BuildId, "player-audio"),
                    OrdinaryPlayerSession ? 0 : world.LevelId,
                    OrdinaryPlayerSession ? _avatarSourceStamp : WorldEntryPreparationPolicy.SourceFileStamp(source?.Path),
                    WorldScenes.Current!.Plan.Request), source is null ? null : assets, _args.FixedSeed,
                    _args.Rac1WrenchSmoke || _args.Rac1WeaponsSmoke || _args.Rac1PlayerAudioSmoke, _args.Rac1PlayerAudioSmoke);
            }));

    private ProfileCoordinator? _profileCoordinator;
    internal ProfileCoordinator ProfileService => _profileCoordinator ??= new(new(
        ProfilePath: () => _args.NoProfile || !OrdinaryPlayerSession ? null :
            _args.ProfilePath is { } path ? Path.GetFullPath(path) :
            DisplayServer.GetName() == "headless" || AutomationRunPolicy.IsAutomated(OS.GetCmdlineUserArgs(), OS.GetCmdlineArgs()) ?
                null : ProjectSettings.GlobalizePath("user://obp-profile.json"),
        OrdinaryPlayer: () => OrdinaryPlayerSession,
        Player: () => _persistentPlayer.Session,
        Native: () => _persistentPlayer.Native,
        AttachedSources: () => [.. Sources.Setup.Library.Attached.Values.Select(source => new ProfileSource(
            source.Game, source.Identity.BuildId, source.Identity.Sha256 ?? throw new InvalidDataException("Source has no authority digest.")))],
        LegacyCampaignExists: () => File.Exists(ProjectSettings.GlobalizePath("user://rac1-campaign.json")),
        CaptureCampaign: previous => new ProfilePayload("rac1/campaign", 1,
            JsonSerializer.SerializeToElement(Rac1CampaignSavePolicy.Capture(_rac1Campaign.Session.Campaign,
                _rac1Campaign.Session.Weapons, _rac1Campaign.Wallet, _rac1Campaign.CollectedNovalisGoldBolts,
                _rac1Campaign.Session.BuriedBolts))).RetainUnknownFields(previous),
        RestoreCampaign: _rac1Campaign.Restore,
        RefreshPlayerSources: RefreshPlayerSourceAvailability,
        WritesSuspended: () => Sources.Setup.ProfileSuspended,
        PlaytestActive: () => _captureReplay?.Recorder is not null,
        WorldActive: () => InWorldMode,
        Log: message => GD.Print(message)));
    private ActorWorldHost? _actorHost;
    internal ActorWorldHost Actors => _actorHost ??= CreateActorHost();
    private ActorWorldHost CreateActorHost() => new(_hostOperations, new(
        () => Sources.Setup.Library, WorldEntryPreparationPolicy.SourceFileStamp, key => _persistentPlayer.Equipment.Sources.GetValueOrDefault(key.SourceGame),
        (live, total, problems) => _navigation.UpdateLabStatus(live, total, problems),
        PlayerLife.RetireActor,
        PlayerLife.Contains, () => _worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.Contacts.Invalidate(),
        id =>
        {
            Rac1World.Context.BoltPickups.Discard(id); Rac1World.Loot.PickupMotions.Remove(id);
            if (Rac1World.Presentation.PickupNodes.Remove(id, out var node) && IsInstanceValid(node)) node.QueueFree();
        }, () => unchecked((uint)(_args.FixedSeed ?? System.Environment.TickCount))), new(
        new(() => _worldScenes?.Current?.Resources.Rac1Gameplay?.Enemies,
            () => Rac1World.Enemies.Targets.Concat(Rac1World.Presentation.CrateNodes), (node, damage) => Rac1World.WeaponDamage.Apply(node, damage),
            key => _worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.SuckCannon.ForgetCapture(key),
            () => Rac1World.Loot.PickupAssets, () => _equipmentPresentation.SelectedAssets,
            (assets, materials) => { Rac1World.Loot.SetDiagnosticAssets(assets, materials); }, delta => Rac1World.Loot.TickRac1Pickups(delta)),
        new(() => _worldScenes?.Current?.Resources.Rac2Gameplay),
        new(() => _worldScenes?.Current?.Resources.Rac3Gameplay)), new(
        () => _player!.GetWorld3D().DirectSpaceState, () => Rac1World.Weapons.Contacts.RayExclusions(), (from, to) => Rac1World.Weapons.Contacts.HasLineOfSight(from, to),
        reason => Rac1World.Context.CombatStatus = reason, () => _player?.Rac1WrenchFrame.Generation,
        () => _playerAudio?.WrenchContact(false)));
    internal GodotPlayerLifecycleHost? LifeIfCreated => _playerLife;
    internal FusionWeaponHost? FusionIfCreated => _fusionWeapons;
}
