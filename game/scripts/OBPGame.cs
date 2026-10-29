using System.Text.Json;
using OBP.Core;
using OBP.Fusion;
using Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Presentation;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.IO;
using OBP.RAC1.Progression;
using OBP.RAC1.Player;
using OBP.Runtime.Gameplay;
using OBP.RAC2;
using OBP.Runtime;
using OBP.Runtime.Presentation;
using OBP.Runtime.Player;
using OBP.Game.Diagnostics;

namespace OBP.Game;

/// <summary>
/// Application root for the trilogy runtime host.
///
/// Production world entry is source-neutral:
/// <c>Game Sources → ObpDestination → IObpWorldProvider → RuntimeWorld → Godot</c>.
/// Source-game parsing ends at the provider boundary; this host owns presentation,
/// input, navigation, diagnostics and the single live-world lifecycle.
///
/// Small <c>--test-scene</c> fixtures remain available for deterministic host
/// checks. Historical GC command-line aliases are translated to canonical
/// destinations rather than selecting a separate runtime path.
/// </summary>
public partial class OBPGame : Node3D
{
    private enum Mode { Smoke, Picker, Selector, World, Title }

    private SourceSetupController? _sourceSetup;
    private SourceSetupController Sources => _sourceSetup ??= CreateSourceSetup();
    public void ShowSourceManagerFromBootstrap() => Sources.ShowForStartup();
    public void RememberCommandLineSource(ObpSourceGame game, string path, bool persist = true) => Sources.Remember(game, path, persist);
    public bool RequireStartupSetup() => Sources.RequireOrdinary("startup");
    private SourceSetupController CreateSourceSetup()
    {
        var setup = new SourceSetupCoordinator(TrilogySourceDefinitions.All,
            _args.SourceConfigPath ?? ProjectSettings.GlobalizePath("user://sources.json"),
            SourceSetupPolicy.Resolve(_args, OS.GetCmdlineUserArgs(), OS.GetCmdlineArgs()), new PhysicalSourceSetupStorage(),
            new GodotSourceSetupConsumers(this, game =>
            {
                _sourceVerification?.Cancel(game);
                FusionWeapons.ResetSources();
                if (game == ObpSourceGame.Rac1)
                {
                    _playerAudio?.RetireWorld();
                    ResetRac1WeaponAudio();
                }
            }, FusionWeapons.PrepareSources),
            message => GD.Print(message), message => GD.PrintErr(message));
        return new(this, setup, new(EnsureRac1CampaignPersistenceInitialized, () =>
        {
            _navigation.CancelNavigation(); _navigation.RetireMenuPages(); _mode = Mode.Picker; _sceneKind = "sources";
            _modals?.RetireAll();
            TeardownWorld(); EnsurePlainEnvironment();
            _navigation.RetireSelector();
            if (_worldHud is not null && IsInstanceValid(_worldHud)) _worldHud.Visible = false;
            _camera.Current = true; _activeCamera = _camera;
        }, ShowTitleScreen, ShowDestinationSelector, ShowControllerSettingsFromMenu, AbandonFailedRac1Travel,
            (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code)),
            () => new(_profileStatus, _profileWriteBlocked, _rac1CampaignRestoreWarning), () => _debugOutputEnabled,
            _hostOperations.Token, DisplayServer.GetName() == "headless" || AutomationRunPolicy.IsAutomated(OS.GetCmdlineUserArgs(), OS.GetCmdlineArgs()));
    }


    private Node3D _worldRoot = null!;
    private Node3D _playerRoot = null!;
    private Node3D _cameraRoot = null!;
    private CanvasLayer _ui = null!;
    private Camera3D _camera = null!;

    private CommandLineArgs _args = new();


    private readonly PlayerSessionHost _persistentPlayer = new();
    private readonly Rac1CampaignHost _rac1Campaign = new();
    private GodotPlayerLifecycleHost? _playerLife;
    private GodotPlayerLifecycleHost PlayerLife => _playerLife ??= CreatePlayerLifecycle();
    private GodotPlayerLifecycleHost CreatePlayerLifecycle() => new(new(
        () => _playerNative, () => _playerSession, () => !_rac1Nanotech.Probe().IsDead,
        () => _activeDestination is { } destination ? OnDestinationChosen(destination) : EnterFusionLab(),
        () => _ui, () => _world?.Environment?.DeathHeight,
        node => node.Source.SourceGame == ObpSourceGame.Rac1
            ? Rac1World.Context.MobyRuntime.TryGet(new(node.Source.NativeClassId, node.Source.InstanceIndex), out var native) && native!.IsActive
            : _worldScenes?.Current?.Resources.Rac2Gameplay?.IsLiveHostile(node) == true,
        () => _rac1CombatRetry is not null,
        () => { FusionWeapons.ClearOnDeath(); ResetRac1WrenchHost(); },
        (before, after) => _playerAudio?.NotifyDamage(before, after), () => RefreshRac1HudState(), _hudState,
        () => Rac1HudProjection.Capture(_rac1Nanotech, _rac1Weapons, capturedMobyCount: _rac1AdditionalWeapons?.Cargo.Count ?? 0,
            swimming: _player?.Rac1SwimState, ownsOxygenMask: _player?.Rac1MovementEquipment.OxygenMask == true),
        FusionWeapons.Hud));
    private FusionWeaponHost? _fusionWeapons;
    private FusionWeaponHost FusionWeapons => _fusionWeapons ??= CreateFusionWeaponHost();
    private FusionWeaponHost CreateFusionWeaponHost() => new(this, new(
        Options: () => new(_playerNative?.Weapons, _captureReplay?.Recorder?.Reader is { Header.Weapons: null },
            _args.FusionTest, _args.FixedSeed ?? 1440, OrdinaryPlayerSession, _activeDestination?.DestinationId,
            _captureReplay?.Recorder is not null, Sources.Setup.Access == SourceAccessMode.OrdinaryPlay || _args.CaptureFrame is not null || _args.ShotsPath is not null),
        Snapshot: () => new(_player is not null, _movementStateLoading, PlayerLife.Alive, _player?.Scripted == true,
            _player?.Rac1WrenchFrame.Active == true, _rac1WrenchThrow.Active, _player?.Rac1WaterHandItemHidden == true,
            QuickSelectOpen, _player?.Rac1EquippedItemHeld == true),
        Inventory: () => _rac1Weapons, Hand: () => _rac1HeldItem.Hand, Equipment: ReadPlayerEquipment,
        SelectNative: OnRac1WeaponSelectionRequested, HoldNative: HoldSelectedRac1HandItem,
        SelectionChanged: previous =>
        {
            _previousSelectedWeapon = previous;
            ResetRac1WeaponAudio();
            Rac1World.Context.BombFireRequested = _rac1AdditionalFireRequested = Rac1World.Context.EquipmentUsePressed = false;
            _rac1BombThrowActive = false;
        },
        RefreshAvailability: () => _persistentPlayer.Equipment.RefreshAvailability(),
        RefreshHeldVisibility: RefreshRac1WrenchPresentationVisibility, RefreshHud: () => RefreshRac1HudState(),
        ClearNativeTransients: _ =>
        {
            if (_worldScenes?.Current?.Resources.Rac1Gameplay is null) return;
            foreach (var (id, projectile) in Rac1World.Presentation.Projectiles.ToArray()) RetireBomb(id, projectile);
            ResetRac1BombPresentation();
            Rac1World.Context.BombGlove = new(_rac1Weapons);
            Rac1World.Context.BombFireRequested = Rac1World.Context.EquipmentUsePressed = false;
        },
        IsolateCampaign: start =>
        {
            var isolated = new Rac1FusionCampaignIsolation(_rac1Campaign, _rac1CollectedNovalisGoldBolts, start);
            _persistentPlayer.WeaponPreview = null;
            return isolated;
        },
        ReloadFixture: enabled =>
        {
            FlushRac1CampaignSaves("before fusion test");
            _args = _args with { FusionTest = enabled };
            ClosePauseMenu();
            OnDestinationChosen(_activeDestination!);
        },
        Report: ReportMovementState, Record: (kind, fact) => CaptureReplay.RecordGameplayFact(kind, fact)),
        new(() => Sources.Setup.Library.Attached.Values, RefreshPlayerSourceAvailability,
            () => ActorHost.InvalidateSources(), () => _equipmentPresentation.BindSelected(_avatarPresentation,
                _persistentPlayer.Equipment.Sources, _rac1EquipmentAssets, FusionWeapons.Sources.Assets)),
        new(new(() => _player, () => EquipmentForward, () => OrdinaryPlayerSession,
            () => OrdinaryPlayerSession
                ? ActorHost.Combat.ReadGeometry().Select(target => new FusionWeaponTargetGeometry(target.Key, target.Bounds, true, false))
                : EquipmentGeometry.Select(target => new FusionWeaponTargetGeometry(
                    new(ObpSourceGame.Rac1, target.Node.Source.NativeClassId, target.Node.Source.InstanceIndex), target.Bounds,
                    Rac1World.Enemies.IsRac1GameplayActive(target.Node), Rac1World.Enemies.Roster.TryGetValue(target.Node.Source.InstanceIndex, out var enemy) && enemy.RewardEmitted)),
            (query, muzzle) => ActorHost.Combat.Trace(query, muzzle), intent =>
            {
                if (OrdinaryPlayerSession)
                {
                    bool accepted = ActorHost.Combat.Apply(intent);
                    CaptureReplay.RecordGameplayFact("damage", new { Intent = intent, Accepted = accepted });
                    return accepted;
                }
                var target = EquipmentTargets.FirstOrDefault(node => node.Source.SourceGame == intent.Target.SourceGame &&
                    node.Source.NativeClassId == intent.Target.NativeClassId && node.Source.InstanceIndex == intent.Target.InstanceIndex);
                return target is not null && FusionDamagePolicy.Translate(intent) is { } damage && ApplyRac1AdditionalDamage(target, damage);
            }, Rac1DamageableRayExclusions, BombHasLineOfSight)), () => _avatarPresentation.View,
        new((nativeClass, name) => CreateRac1EffectNode(nativeClass, name, playerEquipment: true),
            (nativeClass, localSound, position) =>
            {
                if (_playerAudio?.PlayEquipmentSound(nativeClass, localSound, new(-position.X, position.Y, position.Z)) == true)
                    RecordRac1WeaponSound(nativeClass, localSound);
            }, world =>
            {
                var assets = _equipmentPresentation.Assets(OrdinaryPlayerSession, _rac1PickupAssets);
                _equipmentPresentation.ConfigureWorld(world, assets);
                var source = Sources.Setup.Library.Get(ObpSourceGame.Rac1);
                _playerAudio!.Bind(new(new(ObpSourceGame.Rac1, OBP.RAC1.Rac1Authority.Primary.BuildId, "player-audio"),
                    OrdinaryPlayerSession ? 0 : world.LevelId,
                    OrdinaryPlayerSession ? _avatarSourceStamp : SourceFileStamp(source?.Path),
                    WorldScenes.Current!.Plan.Request), source is null ? null : assets, _args.FixedSeed,
                    _args.Rac1WrenchSmoke || _args.Rac1WeaponsSmoke || _args.Rac1PlayerAudioSmoke, _args.Rac1PlayerAudioSmoke);
            }));

    private static System.Numerics.Vector3 ToNumerics(Vector3 value) => new(value.X, value.Y, value.Z);
    private Rac1WorldModule Rac1World => _worldScenes?.Current?.Resources.Rac1Gameplay
        ?? throw new InvalidOperationException("R&C1 gameplay requires an active scene lease.");
    private Rac1BoltWallet _rac1BoltWallet { get => _persistentPlayer.Rac1Wallet(_rac1Campaign.Wallet); set => _rac1Campaign.Wallet = value; }
    private Rac1RatchetNanotechSession _rac1Nanotech => _persistentPlayer.Rac1Health;
    private Rac1WeaponInventory _rac1Weapons => _persistentPlayer.Rac1Weapons(FusionWeapons.Session, FusionWeapons.TestActive, _rac1Campaign.Session.Weapons);
    private Rac1HeldItemSession _rac1HeldItem => _persistentPlayer.Rac1Held;

    private Rac2WorldModule CreateRac2WorldModule(RuntimeWorld world, GodotWorldResources resources) => new(
        world, resources.Scene, resources, new Rac2WorldPorts(
            Current: () => ReferenceEquals(_worldScenes?.Current?.Resources, resources),
            SourceValid: () => Sources.Setup.Library.Get(ObpSourceGame.Rac2)?.Identity.BuildId == OBP.RAC2.Rac2Authority.Primary.BuildId,
            Ordinary: () => OrdinaryPlayerSession,
            Alive: () => PlayerLife.Alive,
            HasNativePlayer: () => _playerNative is not null,
            Actors: () => ActorHost,
            Audio: node => _worldHost.PlayRepresentativeAudioOneShot(node.Source),
            CrateReward: SpawnCrateBoltPickups,
            PlayerDamage: (node, hit, amount, mask) => PlayerLife.ApplySourceDamage(node, OBP.RAC2.Rac2Authority.Primary.BuildId,
                PlayerSessionNativeState.Rac2DamageAdapter, PlayerSessionNativeState.Rac2DamageDescriptor,
                hit, amount, mask).Applied,
            CrateFocus: _args.CrateFocus, CrateAutoStrike: _args.CrateAutoStrike));

    private Rac1WorldModule CreateRac1WorldModule(RuntimeWorld world, RuntimeWorldScene.Result scene) => new(world, scene, Rac1EffectRandom,
        new Rac1WorldResetPorts(
            Player: () =>
            {
                FusionWeapons.ResetWorld();
                if (_playerNative is null) _rac1HeldItem.ResetForLevelEntry();
                else _playerNative.EndWorld();
            },
            Wheel: () => { _lastQuickSelectTap = 0; _previousSelectedWeapon = null; },
            Taxis: ResetRac1MetropolisTaxis, Water: () => _rac1NovalisWater = null,
            Music: ResetRac1NovalisMusic, RedPlants: ResetRac1RedPlants, PulsingOrbs: ResetRac1PulsingOrbs,
            Explosives: ResetRac1ExplosiveCrates, Checkpoints: ResetRac1CheckpointTriggers,
            Missions: ResetRac1Missions, MetalDetector: ResetRac1MetalDetector, Wrench: ResetRac1WrenchHost,
            Retry: ResetRac1CombatRetry, Pickups: ResetRac1PickupPresentation,
            GoldBolts: ResetRac1GoldBolts, Cranks: ResetRac1NovalisCranks, Breakables: ResetRac1NovalisBreakables,
            Mechanisms: () => _rac1NovalisMechanisms.Clear(), AdditionalWeapons: ResetRac1AdditionalWeapons,
            BombPresentation: ResetRac1BombPresentation),
        new Rac1WorldActorPorts(ConfigureRac1PickupPresentation, AddRac1CrateCollider,
            ConfigureRac1ExplosiveCrates, ConfigureRac1CheckpointTriggers, ConfigureRac1Missions, ConfigureRac1MetalDetector,
            ConfigureRac1PlacedBolts, ConfigureRac1GoldBolts, ConfigureRac1RedPlants, ConfigureRac1PulsingOrbs,
            ConfigureRac1NovalisCranks, ConfigureRac1NovalisBreakables, ConfigureRac1NovalisMechanisms,
            ConfigureRac1NovalisWater, ConfigureRac1NovalisMusic, ConfigureRac1MetropolisTaxis),
        new Rac1WorldTickPorts(() => _movementStateLoading, AbortRac1MetropolisTaxiRide,
            TickRac1MetropolisTaxis, TickRac1NovalisMusic, TickRac1Swing, TickRac1RedPlants,
            TickRac1PulsingOrbs, TickRac1ExplosiveCrates, TickRac1CheckpointTriggers, TickRac1MetalDetector,
            TickRac1Pickups, TickRac1GoldBolts, TickRac1Missions),
        new Rac1PlayerGameplayPorts(() => OrdinaryPlayerSession, () => _playerNative is not null, () => PlayerLife.Alive,
            () => _rac1Nanotech, () => _rac1Weapons, _persistentPlayer.Equipment.RefreshMovementEquipment,
            OnRac1PrimaryAttackRequested, OnRac1WeaponSelectionRequested, OnRac1WeaponCycleRequested,
            ToggleRac1WeaponPreview, OnRac1WrenchPhysicsUpdated, FusionWeapons.Configure, () => FusionWeapons.Session is not null,
            FusionWeapons.ConfigureNativePresentation, _persistentPlayer.BeginParityWorld,
            () => TryCompleteRac1EnvironmentalRestart(automatic: true), TickRac1VeldinEnvironmentalDeath,
            () => { FusionWeapons.ClearOnDeath(); ResetRac1WrenchHost(); ShowRac1CombatRetry(); }),
        new Rac1WorldContactPorts(TryTurnRac1NovalisCrank, SampleRac1NovalisWaterHeight, OnRac1NovalisDrowned,
            sound => _playerAudio?.Play(new(0, sound))),
        new Rac1GameplayHudPorts(_hudState, () => OrdinaryPlayerSession, () => _playerSession is not null,
            PlayerLife.PublishHud, FusionWeapons.Hud, () => _rac1Nanotech, () => _rac1Weapons, () => _rac1BoltWallet,
            () => _rac1AdditionalWeapons?.Cargo.Count ?? 0, () => _player, MetalDetectorHudText),
        owner => CreateRac1EnemyHost(world, scene, owner));

    private Rac1EnemyHost CreateRac1EnemyHost(RuntimeWorld world, RuntimeWorldScene.Result scene, Rac1WorldModule owner) => new(
        world, scene, owner.Context, owner.Presentation, new(
            () => Sources.Setup.Library.Get(ObpSourceGame.Rac1)?.Path ?? throw new InvalidOperationException("Enemy assets require the active R&C1 source."),
            () => Rac1EffectRandom,
            new(() => _player, () => PlayerLife.Alive, () => _playerNative is not null, () => _rac1Nanotech,
                (attacker, tick, amount, flags) => PlayerLife.ApplySourceDamage(attacker, OBP.RAC1.Rac1Authority.Primary.BuildId,
                    PlayerSessionNativeState.Rac1DamageAdapter, PlayerSessionNativeState.Rac1DamageDescriptor, tick, amount, flags),
                (before, after) => _playerAudio?.NotifyDamage(before, after), feedback => RefreshRac1HudState(feedback)),
            new(ArsenalEnemyGoal, ArsenalEnemyHitsDecoy,
                enemy => _rac1Decoys.Any(decoy => decoy.Grounded && decoy.View.GlobalPosition.DistanceTo(enemy.Body.GlobalPosition) < 1.8f),
                id => _rac1EquipmentShots.FirstOrDefault(shot => shot.Weapon == Rac1WeaponId.MineGlove && shot.Id == id)?.Position,
                id => _rac1DoomAgents.FirstOrDefault(bot => bot.Id == id)?.Body.GlobalPosition,
                SelectRac1CrateReward, SpawnRac1BoltPickups, (node, ids) => ActorHost.TrackPickups(node, ids),
                nativeClass => _rac1PickupAssets?.Models.ContainsKey(nativeClass) == true,
                (nativeClass, motion) =>
                {
                    var node = CreateRac1EffectNode(nativeClass, $"rac1_enemy_fragment_{nativeClass}");
                    node.GlobalPosition = FromEffectNative(motion.Position); _rac1Debris.Add((node, motion));
                }, PlayRac1PickupSound, (node, sound) => ActorHost.PlayRac1Sound(node, sound), BombHasLineOfSight, origin => SpawnRac1BombExplosion(origin),
                origin => SpawnRac1ExplosionBursts(origin, Rac1BombExplosion.Bursts(false).Take(1).ToArray()),
                TryTriggerRac1ExplosiveDamage, CompleteRac1CrateDamage,
                (id, presence) => { if (_rac1CrateBodies.TryGetValue(id, out var body) && IsInstanceValid(body)) body.CollisionLayer = presence == RuntimeEntityPresence.Active ? 1u : 0; },
                id => _rac1Chickens.ContainsKey(id)),
            new(() => ReferenceEquals(_worldScenes?.Current?.Resources.Rac1Gameplay, owner),
                EndRac1QueenScriptedCamera, ResetRac1FrogCarriers, ResetRac1QueenLinkedChildren,
                () =>
                {
                    _rac1Frogs = null; _rac1FrogShuttle = null;
                    _rac1FrogWaves = null; _rac1FrogQueen = null; _rac1FrogWaveNodes.Clear();
                    _rac1QueenRecovery = null; _rac1QueenEntrance = null;
                    _rac1QueenFlight = null; _rac1QueenPendingCamera = null;
                    _rac1QueenAnimation = null; _rac1QueenNode = null;
                    _rac1QueenLinkedGate = null; _rac1QueenGateNative = null;
                    _rac1QueenLinkedNodes.Clear(); _rac1QueenLinkedBaseRotations.Clear();
                }, () => _rac1AridiaEnemies = new(), ResetRac1AridiaScriptActors, ResetRac1ConstructorbotStartups,
                () => _rac1AridiaEnemies, volumes => _rac1AridiaClipVolumes = volumes, _rac1ConstructorbotStartups, _rac1ConstructorbotNativeWaypoints,
                ConfigureRac1AridiaScriptActors, ConfigureRac1Frogs, ConfigureRac1FrogQueen, ConfigureRac1FrogWaves, ConfigureRac1FrogCarriers,
                () => _rac1Frogs, TickRac1FrogShuttleSwitch, TickRac1FrogWaves, () => TickRac1QueenFlight(), TickRac1QueenRecovery, TickRac1QueenLinkedChildren,
                id => _rac1FrogCarrierChildren.ContainsKey(id), TickRac1FrogCarrier, TickRac1AridiaEnemy, TickRac1Frog,
                HideBuriedRac1AridiaEnemy, RestoreRac1AridiaEnemyVisibility,
                ApplyRac1AridiaWeaponDamage)));

    private void RefreshRac1HudState(params HudFeedbackDraft[] feedback)
    {
        if (_worldScenes?.Current?.Resources.Rac1Gameplay is { } gameplay) gameplay.Hud.Refresh(feedback);
        else if (OrdinaryPlayerSession && _playerSession is not null) PlayerLife.PublishHud(feedback);
    }

    private readonly PlayerAvatarPresentationHost _avatarPresentation = new();
    private readonly PlayerEquipmentPresentationBinder _equipmentPresentation = new();
    private ProfileCoordinator? _profileCoordinator;
    private ProfileCoordinator Profiles => _profileCoordinator ??= new(new(
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
                _rac1Campaign.Session.Weapons, _rac1Campaign.Wallet, _rac1CollectedNovalisGoldBolts,
                _rac1Campaign.Session.BuriedBolts))).RetainUnknownFields(previous),
        RestoreCampaign: restored =>
        {
            _rac1Campaign.Session = new(restored.Campaign, restored.Weapons, Rac1BuriedBoltProgress.Restore(restored.BuriedBoltHistory));
            _rac1Campaign.Wallet = new(restored.Bolts);
            _rac1CollectedNovalisGoldBolts.Clear();
            _rac1CollectedNovalisGoldBolts.UnionWith(restored.NovalisGoldBoltInstances);
        },
        RefreshPlayerSources: RefreshPlayerSourceAvailability,
        WritesSuspended: () => Sources.Setup.ProfileSuspended,
        PlaytestActive: () => _captureReplay?.Recorder is not null,
        WorldActive: () => _mode == Mode.World,
        Log: message => GD.Print(message)));

    // Owner-backed projections keep existing world and diagnostic clients compatible.
    // The avatar stamp and committed destination setters write through to their owners;
    // no duplicate mutable player/profile state lives in the application root.
    private ObpPlayerSession? _playerSession => _persistentPlayer.Session;
    private PlayerSessionNativeState? _playerNative => _persistentPlayer.Native;
    private WorldEntrySession? _worldEntries => _persistentPlayer.WorldEntries;
    private PlayerControlProfile? _playerControlProfile => _persistentPlayer.ControlProfile;
    private PlayerAnimationProfile? _playerAnimationProfile => _persistentPlayer.AnimationProfile;
    private (long Length, long Written)? _avatarSourceStamp
    {
        get => _persistentPlayer.AvatarSourceStamp;
        set => _persistentPlayer.AvatarSourceStamp = value;
    }
    private WorldEntryMode PlayerEntryMode => _persistentPlayer.EntryMode ??
        (_args.DiagnosticPlayer ? WorldEntryMode.Diagnostic :
            _args.NativeParityRequested ? WorldEntryMode.RetailParity : WorldEntryMode.OrdinaryFusion);
    private bool OrdinaryPlayerSession => PlayerEntryMode == WorldEntryMode.OrdinaryFusion;
    internal bool ProfileRecoveryRequired => Profiles.RecoveryRequired;
    private string? _profileStatus => Profiles.Status;
    private bool _profileWriteBlocked => Profiles.WriteBlocked;
    private ObpProfile? _profileDocument => Profiles.Document;
    private string? PlayerSaveNotice => Profiles.SaveNotice;
    private string? SharedProfilePath => Profiles.SharedProfilePath;
    private string? _lastCommittedProfileDestination
    {
        get => Profiles.LastCommittedDestination;
        set => Profiles.LastCommittedDestination = value;
    }

    private CaptureReplayCoordinator? _captureReplay;
    private CaptureReplayCoordinator CaptureReplay => _captureReplay ??= CreateCaptureReplay();
    private GodotCapturePresentation? _capturePresentation;
    private GodotCapturePresentation CapturePresentation => _capturePresentation ??= CreateCapturePresentation();
    private SourceVerificationCoordinator? _sourceVerification;
    private SourceVerificationCoordinator SourceVerification => _sourceVerification ??= new(
        game => SourceVerificationFiles.Stamp(Sources.Setup.Library.Get(game)), SourceVerificationFiles.VerifyRac2,
        () => _loadSummary, (text, error) => { GD.Print($"[verification] {text}"); Sources.SetStatus(text, error); });

    private CaptureReplayCoordinator CreateCaptureReplay() => new(new(
        () => OrdinaryPlayerSession, () => FusionWeapons.Session?.Tick ?? 0,
        () => new(_currentDestinationId!, _world!.BuildId, GetWindow().Size.X, GetWindow().Size.Y,
            OrdinaryPlayerSession ? JsonSerializer.SerializeToElement(new { }) : JsonSerializer.SerializeToElement(
                Rac1CampaignSavePolicy.Capture(_rac1Campaign.Session.Campaign, _rac1Campaign.Session.Weapons,
                    _rac1BoltWallet, _rac1CollectedNovalisGoldBolts, _rac1Campaign.Session.BuriedBolts)),
            JsonSerializer.SerializeToElement(CameraSettingsStore.Current), FusionWeapons.Configuration, OrdinaryPlayerSession ? CaptureSharedProfile() : null),
        () => new(_currentDestinationId, GetTree().Paused, GetWindow().Size.X, GetWindow().Size.Y,
            PauseMenuOpen, QuickSelectOpen, Rac1PlanetMapOpen, ControllerSettingsOpen,
            GetViewport().GuiGetFocusOwner() is Button button ? button.Text : GetViewport().GuiGetFocusOwner()?.GetClass(),
            _quickSelect?.SelectedWeapon,
            new(_rac1Campaign.Session.Campaign.CurrentLevel, _rac1Campaign.Session.Campaign.VisitedPlanets.Select(v => (double)v).ToArray(),
                _rac1Campaign.Session.Campaign.GalacticMap.Select(v => (double)v).ToArray(), _rac1Campaign.Session.Campaign.LevelStates.Select(v => (double)v).ToArray()),
            new(_rac1Nanotech.Probe().Nanotech, _rac1Nanotech.Probe().IsDead, _rac1BoltWallet.Balance,
                _rac1Weapons.CurrentItemId, _rac1HeldItem.NativeItemId ?? -1, _rac1HeldItem.Hand.Equipment?.ToString() ?? "wrench",
                JsonSerializer.SerializeToElement(_rac1Weapons.CapturePersistentState()), JsonSerializer.SerializeToElement(CameraSettingsStore.Current)),
            FusionWeapons.Session is { } fusion ? new(fusion.Selected.ToString(), JsonSerializer.SerializeToElement(FusionWeapons.Configuration),
                FusionWeaponSession.Weapons.Select(key => new CaptureWeaponCheckpoint(key.ToString(), JsonSerializer.SerializeToElement(fusion.Status(key)))).ToArray(),
                [fusion.Tick, fusion.ShotsFired, FusionWeapons.AcceptedHits, FusionWeapons.Projectiles.Count, FusionWeapons.EffectCount], JsonSerializer.SerializeToElement(FusionWeapons.LastDamage),
                JsonSerializer.SerializeToElement(new { fusion.CannonSession.State, Charge = fusion.CannonFrame?.ChargeUpdates ?? 0, Beam = fusion.CannonFrame?.BeamUpdatesRemaining ?? 0 }),
                FusionWeapons.Projectiles.Where(p => IsInstanceValid(p.Value)).Select(p => new CaptureProjectileCheckpoint(p.Key.Generation, p.Key.Sequence, ToNumerics(p.Value.GlobalPosition))).ToArray()) : null,
            _player is { } player && IsInstanceValid(player) ? new(ToNumerics(player.GlobalPosition), ToNumerics(player.Velocity), player.Rac1CurrentYaw, player.Rac1ControlYaw,
                player.IsOnFloor(), player.Rac1RecoveredSupportGrounded, player.Rac1MovementPhase.ToString(), player.AnimationState.ToString(),
                CurrentPlayerAvatarSourceSequence() ?? -1, _avatarPresentation.View?.CurrentFrame ?? -1, _avatarPresentation.View?.FramesPerSecond ?? 0,
                player.Rac1SmallStepDecision, JsonSerializer.SerializeToElement(_rac1Nanotech.Actions.Probe()), JsonSerializer.SerializeToElement(player.Rac1WrenchFrame),
                ToNumerics(player.Camera.GlobalPosition), ToNumerics(player.Camera.GlobalRotation)) : null,
            Rac1World.Context.MobyRuntime.Instances.Select(v => new CaptureMobyCheckpoint(v.NativeClassId, v.InstanceIndex, v.State.NativeState, v.IsActive, v.State.IsTerminalized)).ToArray(),
            (_sceneResult?.DynamicObjectNodes ?? []).Where(v => IsInstanceValid(v.Root)).Select(v => new CaptureObjectCheckpoint(v.Source.NativeClassId, v.Source.InstanceIndex,
                ToNumerics(v.Root.GlobalPosition), ToNumerics(v.Root.GlobalRotation), v.Root.Visible)).ToArray(),
            Rac1World.Enemies.Roster.Where(v => IsInstanceValid(v.Value.Body)).Select(v => new CaptureEnemyCheckpoint(v.Key, ToNumerics(v.Value.Body.Velocity), v.Value.Animation.Sequence,
                v.Value.Animation.Position, v.Value.DeathTicks, v.Value.RewardEmitted, v.Value.Buried,
                v.Value.Node.Source.NativeClassId == 1440 && Rac1World.Enemies.Class1440 is not null ? Rac1World.Enemies.Class1440.Get(v.Key).Health : null,
                Rac1World.Presentation.HostileNodes.ContainsKey(v.Key) ? Rac1World.Context.Hostiles.Probe(v.Value.Node.Source).Health : null)).ToArray(),
            [Rac1World.Presentation.Projectiles.Count, _rac1PickupMotion.Count, _rac1EquipmentShots.Count, _rac1DoomAgents.Count, _rac1AmmoPickups.Count, _rac1NanotechClouds.Count],
            Rac1World.Presentation.Projectiles.Select(v => new CaptureBombCheckpoint(v.Key, ToNumerics(FromEffectNative(v.Value.Motion.Position)), ToNumerics(FromEffectNative(v.Value.Motion.Velocity)),
                v.Value.Motion.NativeState, v.Value.Motion.BlastAge)).ToArray(),
            _rac1EquipmentShots.Select(v => new CaptureShotCheckpoint(v.Id, ToNumerics(v.Position), ToNumerics(v.Step), (int)v.Weapon, v.Age, v.Grounded)).ToArray(),
            _rac1DoomAgents.Where(v => IsInstanceValid(v.Body)).Select(v => new CaptureBodyCheckpoint(v.Id, ToNumerics(v.Body.GlobalPosition), ToNumerics(v.Body.Velocity))).ToArray(),
            Rac1World.Presentation.PickupNodes.Where(v => IsInstanceValid(v.Value)).Select(v => new CapturePositionCheckpoint(v.Key, ToNumerics(v.Value.GlobalPosition))).ToArray(),
            _rac1NovalisCrankNodes.Keys.Select(v => new CaptureCrankCheckpoint(v, _rac1NovalisCranks.Progress(v))).ToArray()),
        () => new(ProfilePlayer.Capture(_playerSession!.Capture()), CaptureProfileSources(), ActorHost.ReadActors().Select(actor => new CaptureActorCheckpoint(
            actor.Registration.Instance.Target, actor.State.Position, actor.State.Alive, actor.State.Retired, actor.State.Health ?? -1, actor.State.NativeState, actor.State.NativeSequence ?? -1)).ToArray(), ActorHost.TickCount),
        () => Sources.Setup.Library.Attached.Values.Select(source => source.Identity).ToArray(), () => ActorHost.ReadReplayAdapters(),
        () => GodotCaptureEffects.Readiness(FusionWeapons.Sources.Preparation is not null, ActorHost, _player is not null && _world is not null),
        new(() => GodotCaptureEffects.Prepare(_worldScenes?.Current?.Resources), () => _worldScenes?.Current?.Resources.Effects?.ReplayIdentity,
            values => _worldScenes?.Current?.Resources.Effects?.AddCheckpoint(values)),
        new(destination => destination == "obp:fusion-lab" ? EnterFusionLab() : TrilogyWorldProviders.Resolve(destination) is { } native && OnDestinationChosen(native),
            item => SelectPlayerEquipment(item).Accepted, (item, enabled) => SetPlayerGadgetEnabled(item, enabled).Accepted,
            (slot, item) => AssignPlayerQuickSelect(slot, item).Accepted, () => UnequipPlayerEquipment().Accepted,
            operation => _worldScenes?.Current?.Resources.Effects?.Control(operation).Accepted == true),
        destination => TrilogyWorldProviders.Resolve(destination) is not null,
        () => Sources.Setup.Library.Attached.Values.Select(source => SourceVerificationFiles.Stamp(source)
            ?? throw new InvalidDataException("Recording source attachment is unavailable.")).ToArray()),
        GodotCaptureReplayStartup.Environment(), CaptureBuildIdentity.Read(typeof(OBPGame).Assembly));

    private PlaytestDriverPorts CreatePlaytestDriverPorts() => new(new(this, () => _player, () => _pauseMenu), _hostOperations, () => OrdinaryPlayerSession,
        () => _movementStateLoading, () => _bookmarkReplayStep,
        new(() => _bugReporting?.ReplayReport, report => _bugReporting!.OpenForm(report), () => _bugReporting!.OpenInbox(), () => _bugReporting?.Close()),
        () => _world?.BuildId, () => FusionWeapons.Configuration,
        () => (_currentDestinationId!, _activeDestination?.DisplayName ?? "Fusion Lab"),
        () => new MovementReproState(MovementReproState.CurrentVersion, _currentDestinationId!, _world!.BuildId, DateTimeOffset.UtcNow,
            CaptureReplay.Build.AssemblyId, _player!.CaptureMovementRepro())
        { Profile = JsonSerializer.Deserialize<JsonElement>(CaptureSharedProfile().Encode()) },
        ReportMovementState, new((fusion, token) => CreateCaptureDiagnosticHarness().StartRecording(fusion, token),
            token => CreateCaptureDiagnosticHarness().StartReports(token), token => CreateCaptureDiagnosticHarness().StartReconciliation(token),
            token => CreateCaptureDiagnosticHarness().VerifyFinishedContinuationAsync(token),
            error => CreateCaptureDiagnosticHarness().Acceptance.Fail("capture-route", error, "playtest-save-failed")));

    private GodotCapturePresentation CreateCapturePresentation() => new(this, () => new(_frame, CaptureHarness.ActiveRenderer(),
        (string)Engine.GetVersionInfo()["string"], _world?.BuildId ?? Rac2Authority.Primary.BuildId, _world?.Game,
        _world?.PlanetName, _world?.LocationName, _world?.LevelId, _worldSwitches, ToNumerics(_activeCamera.GlobalPosition), _overlay?.StatusLine(),
        _worldHost.AnimationClockSeconds, _worldHost.AnimationStates().Select(a => new CaptureAnimationSnapshot(a.Name, a.CurrentFrame, a.FrameCount, a.Playing)).ToArray(),
        _sceneResult?.MeshInstances ?? 0, _sceneResult?.Triangles ?? 0, _sceneResult?.Textures ?? 0, _sceneResult?.CollisionBodies ?? 0, _sceneResult?.CollisionTriangles ?? 0,
        _sceneResult?.TieInstances ?? 0, _sceneResult?.ShrubInstances ?? 0, _sceneResult?.MobyInstances ?? 0, _sceneResult?.DynamicObjects ?? 0,
        _worldScenes?.Current?.Resources is { } resources && resources.CrateDiagnostics.Capture(resources.Rac2CrateBolts) is { } crates ? JsonSerializer.SerializeToElement(crates) : null,
        JsonSerializer.SerializeToElement(_worldScenes?.Current?.Resources.Rac1Gameplay?.Hud.Snapshot()), JsonSerializer.SerializeToElement(_hudState.Current),
        _player is { } player && IsInstanceValid(player) ? new(ToNumerics(player.GlobalPosition), player.IsOnFloor(), player.MovementControllerLabel, player.CameraControllerLabel,
            player.HasActiveRecoveredCamera, player.Rac1RuntimeCameraState?.ControlHeadingRadians, player.Rac1RuntimeCameraState?.PreferredDistance,
            player.Rac1RuntimeCameraState?.EffectiveDistance, player.Rac1CameraManualYaw, player.Rac1CameraManualPitch, player.Rac1CameraObstructionCorrection,
            player.Rac1CameraObstructionReleaseTicks, player.Rac1LocomotionState.ToString(), player.Rac1YawMode.ToString(), player.AnimationState.ToString()) : null),
        () => _sceneKind, () => _args.CaptureOut);

    private void EnsurePlayerSession()
    {
        if (_playerSession is not null) return;
        Sources.Initialize();
        if (!_persistentPlayer.Ensure(PlayerEntryMode, _rac1Campaign.Session.Weapons,
            _rac1Campaign.Wallet.Balance, _args.FixedSeed ?? 1440, new PlayerCameraPreferences())) return;
        if (_playerNative is not null) FusionWeapons.BindPersistent(_playerNative.Weapons);
        RefreshPlayerSourceAvailability();
        InitializeSharedProfile();
        if (_captureReplay?.TakeStartingProfile() is { } capture) ApplySharedProfile(capture);
    }

    private void RefreshPlayerSourceAvailability()
    {
        _playerSession?.SetAttachedSources(Sources.Setup.Library.Attached.Values.Select(source => source.Identity));
        _playerNative?.RefreshDevelopmentSources(_persistentPlayer.Equipment.Sources);
        _persistentPlayer.Equipment.RefreshAvailability();
    }

    // Stable UI/diagnostic entrypoints delegate to the persistent equipment owner.
    public IReadOnlyList<SourceWeaponKey?> ReadPlayerQuickSlots() => _persistentPlayer.Equipment.ReadQuickSlots();
    public IReadOnlyList<EquipmentCatalogEntry> ReadPlayerEquipment() => _persistentPlayer.Equipment.Read();
    public EquipmentChangeResult SelectPlayerEquipment(string catalogId) => _persistentPlayer.Equipment.Select(catalogId);
    public EquipmentChangeResult AssignPlayerQuickSelect(int slot, string? catalogId) => _persistentPlayer.Equipment.AssignQuickSelect(slot, catalogId);
    public EquipmentChangeResult UnequipPlayerEquipment() => _persistentPlayer.Equipment.Unequip();
    public EquipmentChangeResult SetPlayerGadgetEnabled(string catalogId, bool enabled) => _persistentPlayer.Equipment.SetGadgetEnabled(catalogId, enabled);
    public string? PlayerEquipmentConfigurationIdentity => _persistentPlayer.Equipment.ConfigurationIdentity;

    private void InitializeSharedProfile() => Profiles.Initialize();
    private void ApplySharedProfile(ObpProfile profile) => Profiles.Apply(profile);
    private ObpProfile CaptureSharedProfile() => Profiles.Capture();
    private IReadOnlyList<ProfileSource> CaptureProfileSources() => Profiles.CaptureProfileSources();
    private bool SaveSharedProfile(string reason) => Profiles.Save(reason);
    private void TickSharedProfile(double delta) => Profiles.Tick(delta);

    public bool TryResumeSharedProfileFromBootstrap()
    {
        if (!Sources.RequireOrdinary("profile resume")) return false;
        EnsurePlayerSession();
        if (Profiles.WriteBlocked || Profiles.Document?.Destination is not { } destination) return false;
        if (destination == "obp:fusion-lab") return EnterFusionLab();
        return TrilogyWorldProviders.Resolve(destination) is { } native && OnDestinationChosen(native);
    }
    internal CommandLineArgs StartupArgs => _args;

    private long _frame;
    private Mode _mode = Mode.Smoke;

    // capture / smoke bookkeeping
    private string _sceneKind = "smoke";
    private readonly HostOperationScope _hostOperations = new();
    private BugReportUiCoordinator? _bugReporting;
    internal bool BugReportModalOpen => _bugReporting?.IsOpen == true || _captureReplay?.Recorder?.BugInputSuspended == true;
    private ActorWorldHost? _actorHost;
    private ActorWorldHost ActorHost => _actorHost ??= CreateActorHost();
    private ApplicationNavigationCoordinator _navigation = null!;
    private Rac1PlayerAudioPresenter? _playerAudio;

    // the live world
    private readonly WorldHost _idleWorldHost = new();
    private WorldHost _worldHost => _worldScenes?.Current?.Resources.Presentation ?? _idleWorldHost;
    private readonly HudStateAdapter _hudState = new();
    private RuntimeWorld? _world => _worldScenes?.Current?.Plan.Prepared.World;
    private RuntimeWorldScene.Result? _sceneResult => _worldScenes?.Current?.Resources.Scene;
    private DebugOverlay? _overlay;
    private WorldInspectorPanel? _inspector;
    private PlayerHost? _player;
    private int _worldSwitches;

    private Camera3D _activeCamera = null!;
    private Label? _worldHud;
    private PlayerHud? _playerHud;
    private string _loadSummary = string.Empty;

    public override void _Ready()
    {
        ApplicationLifecycle.InstallWindowCloseInterception(GetTree());
        ApplicationLifecycle.SetQuitPreparation(PrepareForQuitAsync);
        try { _args = CommandLineArgs.Parse(OS.GetCmdlineUserArgs()); }
        catch (ArgumentException error)
        {
            GD.PrintErr("[cli] " + error.Message);
            GetNodeOrNull<Node>("SourceManagerBootstrap")?.Free();
            ApplicationLifecycle.RequestQuit(this, "cli-invalid", 2);
            return;
        }
        _persistentPlayer.BindEquipment(new(
            Native: () => _playerNative, Inventory: () => _rac1Weapons, Held: () => _rac1HeldItem,
            Weapons: () => FusionWeapons.Session, CurrentWeapon: () => CurrentWeaponKey, FusionTest: () => FusionWeapons.TestActive,
            Sources: () => Sources.Setup.Library.Attached.Values.ToDictionary(source => source.Game, source => source.Identity.BuildId),
            SelectedAvatar: () => _playerSession?.Capture().Configuration.Avatar,
            PresentedAvatar: () => _avatarPresentation.Avatar?.Identity,
            Presentation: key => _equipmentPresentation.Observe(key, _equipmentPresentation.ModelAvailable(key, _avatarPresentation, _rac1EquipmentAssets, FusionWeapons.Sources.Assets), _equipmentPresentation.Assets(OrdinaryPlayerSession, _rac1PickupAssets) is not null),
            Context: () => new(_player?.HasRac1SwingshotTargets == true, _rac1NovalisWater is not null),
            SelectWeapon: FusionWeapons.Select,
            ResetWeaponPresentation: () =>
            {
                _rac1EquipmentUseTicks = 0; _rac1ArsenalHeld = _rac1PyroEmitting = false;
                ResetRac1WeaponAudio(); FusionWeapons.ResetHeldUse();
                RefreshRac1WrenchPresentationVisibility(); RefreshRac1HudState();
            },
            ApplyMovementEquipment: (movement, backpack) => _player?.ApplyPlayerEquipment(movement, backpack),
            Record: command => CaptureReplay.RecordCommand(command)));
        _playerAudio ??= new(new GodotRac1PlayerAudioPlayback(this, () => _player, intent => _worldHost.PlayEffect(intent)),
            () => _avatarPresentation.Clock?.Tick ?? 0, () => ResetRac1WeaponAudio(),
            reason => GD.Print("[player-audio] " + reason));
        if (!InitializeReplayBookmark()) return;
        if (_args.FusionTest && _args.Destination is null) _args = _args with { Destination = "rac1:LEVEL0" };
        var bugStartup = BugReportStartup.Initialize(this, _args, _hostOperations.Token);
        _args = bugStartup.Args;
        if (!bugStartup.Ready) return;
        if (bugStartup.Capture is { } capture)
            _bugReporting = new(capture, _hostOperations, new(this, () => (Node)_captureReplay!.Recorder!, () => _captureReplay!.Recorder!.BugWriter,
                () => _captureReplay!.Recorder!.ResumeAfterBugOverlay(), notice => _captureReplay!.Recorder!.ShowBugNotice(notice),
                CaptureReplay.Build.Matches, GodotCaptureReplayStartup.PhysicsEngine, WaitForWorldPreparationFrameAsync,
                (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code)));
        var playtestStartup = GodotCaptureReplayStartup.Initialize(this, CaptureReplay, _args, CreatePlaytestDriverPorts());
        _args = playtestStartup.Args;
        if (!playtestStartup.Ready) return;
        if (_args.FusionTest && (_args.Destination != "rac1:LEVEL0" || _args.LoadMovementState is not null))
        {
            GD.PrintErr("[fusion] Choose fresh --destination rac1:LEVEL0 without a movement-state load.");
            GetNodeOrNull<Node>("SourceManagerBootstrap")?.Free();
            ApplicationLifecycle.RequestQuit(this, "fusion-invalid-start", 2);
            return;
        }
        if (_args.ReplayForwardFrames.HasValue && _args.LoadMovementState is null)
        {
            GD.PrintErr("[movement-replay] --replay-forward requires --load-movement-state.");
            ApplicationLifecycle.RequestQuit(this, "movement-replay-missing-state", 2);
            return;
        }
        if (_args.FixedSeed is { } seed)
        {
            GD.Seed((ulong)seed);
        }

        BuildSkeleton();
        _navigation = new(new ApplicationNavigationPorts(
            Host: this, Sources: () => Sources, Args: () => _args,
            PrepareLater: (work, report) => WorldScenes.PrepareLater(work, report),
            World: () => _world, SceneResult: () => _sceneResult,
            Ui: () => _ui, Player: () => _player,
            Campaign: () => _rac1Campaign.Session, Frame: () => _frame,
            IsWorld: () => _mode == Mode.World, IsSelector: () => _mode == Mode.Selector,
            PauseMenuOpen: () => PauseMenuOpen, ControllerSettingsOpen: () => ControllerSettingsOpen,
            QuickSelectOpen: () => QuickSelectOpen, DebugOutput: () => _debugOutputEnabled,
            SaveNotice: () => PlayerSaveNotice, CurrentDestinationId: () => _worldScenes?.DestinationId,
            FusionPreparing: () => FusionWeapons.Sources.Preparation is not null, FusionFailure: () => FusionWeapons.Sources.Failure,
            CancelPreparation: () => _worldScenes?.CancelPreparation(),
            AbandonFailedTravel: AbandonFailedRac1Travel, ReportFailure: ReportWorldPreparationFailure,
            EnterWorld: (id, load, destination, sourcePath, lab) => EnterWorld(id, load, destination, sourcePath, lab),
            EnterPage: isTitle =>
            {
                _modals?.RetireAll();
                _mode = isTitle ? Mode.Title : Mode.Selector;
                _sceneKind = isTitle ? "title" : "destinations";
                TeardownWorld();
                EnsurePlainEnvironment();
                Sources.RetireView();
                if (_worldHud is not null && IsInstanceValid(_worldHud)) _worldHud.Visible = false;
                _camera.Current = true;
                _activeCamera = _camera;
                ApplicationPointer.MouseMode = Input.MouseModeEnum.Visible;
            }));
        if (!Sources.InitializeStartup(_args))
        {
            GetNodeOrNull<Node>("SourceManagerBootstrap")?.Free();
            return;
        }
        if (GetNodeOrNull<SourceManagerBootstrap>("SourceManagerBootstrap") is { } bootstrap)
            bootstrap.StartupVisibilityCheck = () => CreateUiDiagnosticHarness().Start("rac1-startup-visibility-smoke");
        if (_args.OrdinaryUiSmoke) CreateUiDiagnosticHarness().Start("ordinary-ui-smoke");

        // Dedicated deterministic fixture through the same playable Lab host
        // used by the ordinary World Select button.
        if (_args.TestScene == "fusion-lab")
        {
            if (EnterFusionLab() && _args.CaptureFrame is { } fusionCapture)
            {
                if (_player is { } player) StartFusionLabTraversalSmoke(player);
                _hostOperations.Start("capture", token => CapturePresentation.RunCaptureAsync(fusionCapture, token), lifetime: CancellationToken.None);
            }
            return;
        }

        // A --shots run works for any provider and drives the same neutral
        // destination/provider path used by ordinary world entry.
        if (_args.ShotsPath is { } shotList)
        {
            _hostOperations.Start("shots", token => CreateShotListCliService().RunShotsAsync(shotList, token), lifetime: CancellationToken.None);
            return;
        }

        bool bootstrapOwnsScene =
            _args.Destination is not null ||
            _args.Rac2Iso is not null ||
            _args.TestScene is "picker" or "worlds";

        if (_args.TestSceneExplicit && !bootstrapOwnsScene)
        {
            // Keep small deterministic host fixtures independent of retail data.
            // A GC-backed --test-scene player run is handled by the neutral
            // source/destination bootstrap instead.
            BuildScene(_args.TestScene);
        }
        else
        {
            // Production/navigation startup is owned by SourceManagerBootstrap.
            // Keep only a neutral background until its deferred route is ready.
            EnsurePlainEnvironment();
        }

        GD.Print($"[OBPGame] ready — mode={_mode} scene='{_sceneKind}' " +
                 $"capture={_args.CaptureFrame?.ToString() ?? "off"} renderer={CaptureHarness.ActiveRenderer()} " +
                 $"providers={string.Join(",", TrilogyWorldProviders.All.Select(provider => provider.BuildId))}");

        if (_args.CaptureFrame is { } frame)
        {
            Engine.MaxFps = 60;
            _hostOperations.Start("capture", token => CapturePresentation.RunCaptureAsync(frame, token), lifetime: CancellationToken.None);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_mode != Mode.World || _movementStateLoading || _player is null || _player.DevelopmentControls.FlyEnabled) return;
        StepRac1NovalisMechanisms();
        _playerAudio?.StepReactions();
        // Avatar updates stop at death; detached source particles still age out.
        if (!_player.Rac1GameplayAlive) StepRac1BombEffectsPhysics();
        ActorHost.Step(ToNumerics(_player.GlobalPosition), PlayerLife.Alive, _movementStateLoading);
    }

    public override void _Process(double delta)
    {
        GodotCapturePresentation.ReportHostOperationFailures(this, _hostOperations);
        if (_hostOperations.Stopping) return;
        _frame++;
        Sources.Tick(delta, _mode is Mode.World or Mode.Selector or Mode.Title);
        FusionWeapons.TickPreparation();
        ActorHost.PollPreparation();
        _worldScenes?.Current?.Resources.Effects?.PrepareTick();
        TickSharedProfile(delta);
        TickRac1CampaignSaves(delta);

        if (_mode == Mode.World)
        {
            if (_captureReplay?.Recorder is { WaitingForEntry: true } or { PreparingWorld: true }) return;
            // Sky-follow, animated mobies and per-region hero light / fog.
            _worldHost.Tick(delta, _activeCamera?.GlobalPosition ?? Vector3.Zero);
            _worldScenes?.Current?.Resources.Effects?.Render();
            _worldScenes?.Current?.Resources.Rac2Gameplay?.Tick(delta);
            _worldScenes?.Current?.Resources.Rac1Gameplay?.Tick(delta);
            // R&C3 class-500 is an event-driven native consumer; no host frame tick.
            UpdatePlayerHud();
            UpdateWorldHud();
        }

        _sourceVerification?.Tick(Time.GetTicksMsec());
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (TryHandleRac1MetropolisTaxiInput(@event))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        if (key.Keycode == Key.Escape)
        {
            if (TryHandleInteractiveBack())
            {
                return;
            }

            bool interactiveSurface =
                (_mode == Mode.World || _mode == Mode.Selector || _mode == Mode.Picker) &&
                _args.CaptureFrame is null;
            if (ApplicationLifecycle.ResolveEscapeFallback(interactiveSurface) ==
                ApplicationLifecycle.EscapeFallback.StayAlive)
            {
                ApplicationLifecycle.ReportStayAlive(
                    $"unhandled-escape mode={_mode} scene={_sceneKind}");
                return;
            }

            ApplicationLifecycle.RequestQuit(this, "escape-noninteractive");
            return;
        }

        if (_mode != Mode.World || !_debugOutputEnabled)
        {
            return;
        }

        if (key.Keycode == Key.M && TryToggleRac1MetalDetector())
        {
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.K)
        {
            _worldHost.SetAnimationPlaying(!_worldHost.AnimationPlaying);
            UpdateWorldHud();
        }
        else if (key.Keycode == Key.I)
        {
            _inspector?.Toggle();
            // Native R&C3 seed/pose diagnostics run only as explicit scenarios.
        }
        else if (_overlay is { } overlay && HandleOverlayKey(key.Keycode, overlay))
        {
            UpdateWorldHud();
        }
    }

    public bool TryHandleInteractiveBack()
    {
        switch (Modals.HandleBack())
        {
            case ModalBackResult.WheelClosed:
                return true;
            case ModalBackResult.SettingsClosed:
                ApplicationLifecycle.ReportNavigation("controller-settings-close");
                return true;
            case ModalBackResult.PauseClosed:
                ApplicationLifecycle.ReportNavigation("pause-menu-close");
                return true;
        }

        if (TryCloseRac1PlanetMap())
        {
            ApplicationLifecycle.ReportNavigation("rac1-planet-map-close");
            return true;
        }

        if (TryOpenPauseMenu())
        {
            ApplicationLifecycle.ReportNavigation("world-pause-menu-open");
            return true;
        }

        if (TryReturnWorldToDestinations())
        {
            ApplicationLifecycle.ReportNavigation("world-to-destinations");
            return true;
        }

        if (TryReturnSelectorToMenu())
        {
            ApplicationLifecycle.ReportNavigation("selector-to-menu");
            return true;
        }

        return false;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            bool automated = WorldSmokeHarness.IsRequested(_args) ||
                _args.Rac1StartupVisibilitySmoke || _args.CaptureFrame is not null ||
                _args.ShotsPath is not null || _args.StressSwitch is not null;
            ApplicationLifecycle.RequestQuit(this, "desktop-window-close", automated ? 1 : 0);
        }
    }

    private void ReleaseAudioForExit()
    {
        _modals?.RetireAll();
        _hostOperations.Dispose();
        _sourceSetup?.Dispose();
        _bugReporting?.Dispose();
        _sourceVerification?.Dispose();
        SaveSharedProfile("clean exit");
        // An external exit can bypass normal world teardown. Finish campaign
        // persistence before shutting down its owning Godot session.
        FlushRac1CampaignSaves("clean exit");
        ActorHost.Reset();
        _worldHost.Unload(releaseAudioImmediately: true);
        _playerAudio?.Dispose();
        ResetRac1WeaponAudio(freeImmediately: true);
        FusionWeapons.ResetWorld(freeImmediately: true);
        FusionWeapons.ResetSources();
        _worldScenes?.Dispose();
    }

    private async Task PrepareForQuitAsync()
    {
        // Stop gameplay before releasing its audio, otherwise a final physics
        // update can start another footstep/voice during shutdown.
        ProcessMode = ProcessModeEnum.Disabled;
        _modals?.RetireAll();
        _captureReplay?.Recorder?.SuspendBugCaptureForQuit();
        _bugReporting?.Dispose();
        Task verificationStopped = _sourceVerification?.StopAsync() ?? Task.CompletedTask;
        Task hostStopped = _hostOperations.StopAsync();
        if (_worldScenes is { } scenes)
        {
            scenes.CancelPreparation();
            await scenes.PendingPreparation;
        }
        await hostStopped;
        await verificationStopped;
        GodotCapturePresentation.ReportHostOperationFailures(this, _hostOperations);
        if (_captureReplay?.Recorder is { } playtest)
            await _hostOperations.ObserveQuitWriteAsync("capture-flush", playtest.PrepareBugCaptureQuitAsync());
        GodotCapturePresentation.ReportHostOperationFailures(this, _hostOperations);
        ReleaseAudioForExit();

        // Godot 4.7.2 retires stopped playbacks on the mixing thread, then
        // releases their references in AudioServer's main-thread update. Tree
        // teardown alone cannot guarantee either has run. Observe two mixes
        // before the final quit; use wall time even with --fixed-fps.
        static double LastMixTime() => Time.GetTicksUsec() / 1_000_000d - AudioServer.GetTimeSinceLastMix();
        double lastMix = LastMixTime();
        ulong deadline = Time.GetTicksMsec() + 1000;
        int mixes = 0;
        while (mixes < 2 && Time.GetTicksMsec() < deadline)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            double mix = LastMixTime();
            if (mix > lastMix + .001d)
            {
                mixes++;
                lastMix = mix;
            }
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print($"[lifecycle] audio retired before quit (mixes={mixes})");
    }

    public override void _ExitTree()
    {
        // Also cover external/engine exits which bypass RequestQuit.
        try
        {
            ReleaseAudioForExit();
        }
        finally
        {
            try { _playerLife?.Dispose(); }
            finally
            {
                try { _fusionWeapons?.Dispose(); }
                finally
                {
                    _persistentPlayer.RetireEquipment();
                    ApplicationLifecycle.SetQuitPreparation(null);
                    ApplicationLifecycle.ReportRootExit(Name);
                }
            }
        }
    }

    /// <summary>F1..F7 + J toggle the <see cref="DebugOverlay"/> inspection layers in a loaded world.</summary>
    private static bool HandleOverlayKey(Key keycode, DebugOverlay overlay)
    {
        switch (keycode)
        {
            case Key.F1: overlay.IsolateNextKind(); return true;
            case Key.F2: overlay.Toggle(DebugOverlay.Layer.KindTint); return true;
            case Key.F3: overlay.Toggle(DebugOverlay.Layer.CollisionWire); return true;
            case Key.F4: overlay.Toggle(DebugOverlay.Layer.WorldBounds); return true;
            case Key.F5: overlay.Toggle(DebugOverlay.Layer.EnvGizmos); return true;
            case Key.F6: overlay.Toggle(DebugOverlay.Layer.HideSky); return true;
            case Key.F7: overlay.Clear(); return true;
            case Key.J: overlay.Toggle(DebugOverlay.Layer.Skeleton); return true;
            default: return false;
        }
    }

    private void BuildSkeleton()
    {
        _worldRoot = new Node3D { Name = "WorldRoot" };
        _playerRoot = new Node3D { Name = "PlayerRoot" };
        _cameraRoot = new Node3D { Name = "CameraRoot" };
        _ui = new CanvasLayer { Name = "UI" };
        AddChild(_worldRoot);
        AddChild(_playerRoot);
        AddChild(_cameraRoot);
        AddChild(_ui);

        _camera = new Camera3D { Name = "Camera3D", Current = true, Far = 12000f };
        _cameraRoot.AddChild(_camera);
        _activeCamera = _camera;
    }

    /// <summary>
    /// Remove deterministic smoke/bootstrap presentation before a production
    /// provider world is adopted. These nodes are not part of RuntimeWorld and
    /// must never leak into a direct destination capture.
    /// </summary>
    private void ClearBootstrapSceneArtifacts()
    {
        GetNodeOrNull<Node>("PlainEnvironment")?.QueueFree();
        GetNodeOrNull<Node>("Sun")?.QueueFree();
        _worldRoot.GetNodeOrNull<Node>("Floor")?.QueueFree();
        _worldRoot.GetNodeOrNull<Node>("SmokeCube")?.QueueFree();
        _ui.GetNodeOrNull<Node>("Banner")?.QueueFree();
    }

    /// <summary>Free the current world sub-tree and everything it owns. Safe to call when nothing is loaded.</summary>
    private void TeardownWorld() => WorldScenes.Retire();

    private void RetireWorldBindings()
    {
        FlushRac1CampaignSaves("world teardown");
        ActorHost.Reset();
        PlayerLife.RetireWorld();
        ClearFusionLabStatus();
        CloseWeaponQuickSelect();
        TearDownInteractiveOverlays();
        TryCloseRac1PlanetMap();
        ClearPlayerAvatarView();
        if (_worldScenes?.Current?.Resources is { } resources)
        {
            try { resources.Rac1Gameplay?.Dispose(); }
            finally { resources.Rac1Gameplay = null; }
            try { resources.Rac2Gameplay?.Dispose(); }
            finally { resources.Rac2Gameplay = null; }
        }
        if (!OrdinaryPlayerSession) _hudState.ResetSession();
        _playerHud?.Render(_hudState.Current);
        // Retire the old collision object before a same-frame replacement can
        // snap its ground ray onto that queued-for-deletion player's capsule.
        if (_player?.GetParent() is { } parent) parent.RemoveChild(_player);
        _player?.QueueFree();
        _player = null;

        _overlay = null; // its nodes live under the world sub-tree that was just freed
        _inspector?.QueueFree();
        _inspector = null;
        if (_worldScenes?.Current?.Resources is { } rac3Resources)
        {
            try { rac3Resources.Rac3Gameplay?.Dispose(); }
            finally { rac3Resources.Rac3Gameplay = null; }
        }    }

    /// <summary>
    /// Attach the runtime inspection overlay to a freshly-built world and apply
    /// any <c>--overlay</c> layers requested for a deterministic capture.
    /// F1..F7 toggle the layers interactively; see <see cref="DebugOverlay"/>.
    /// </summary>
    private void SetupOverlay(RuntimeWorldScene.Result result, RuntimeWorld world)
    {
        _overlay = new DebugOverlay(result, world);
        ApplyOverlaySpec(_args.Overlay);

        _inspector = new WorldInspectorPanel(result, world, _worldHost);
        _ui.AddChild(_inspector);
        if (_args.Inspect && _debugOutputEnabled)
        {
            CallDeferred(nameof(OpenInspectorDeferred));
        }
    }

    private void OpenInspectorDeferred()
    {
        if (_debugOutputEnabled) _inspector?.Toggle();
    }

    /// <summary>
    /// Reset the overlay and apply a comma-separated layer / <c>isolate:&lt;kind&gt;</c>
    /// spec (the <c>--overlay</c> syntax). Used for one-shot captures and per shot
    /// in a <c>--shots</c> run.
    /// </summary>
    private void ApplyOverlaySpec(string? spec)
    {
        if (_overlay is not { } overlay)
        {
            return;
        }

        overlay.Clear();
        if (spec is not { Length: > 0 })
        {
            return;
        }

        foreach (string token in spec.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
        {
            if (token.StartsWith("isolate:", System.StringComparison.OrdinalIgnoreCase))
            {
                string want = token["isolate:".Length..];
                if (!overlay.TryIsolateKind(want))
                {
                    GD.PrintErr($"[OBPGame] unknown --overlay isolate kind '{want}' (valid: {string.Join(", ", DebugOverlay.IsolationCycle[1..])})");
                }
            }
            else if (System.Enum.TryParse<DebugOverlay.Layer>(token, ignoreCase: true, out var layer))
            {
                overlay.Set(layer, true);
            }
            else
            {
                GD.PrintErr($"[OBPGame] unknown --overlay token '{token}'");
            }
        }
    }

    /// <summary>
    /// Per-planet establishing shot for the regression capture set: a fixed
    /// camera at the native ship point, lifted and pulled back, looking at the
    /// trusted-geometry centre. Deterministic — no player, no physics settle.
    /// </summary>
    private void FrameShowcaseCamera(RuntimeWorld world)
    {
        RuntimeWorldScene.FrameCamera(_camera, world.Bounds, azimuthDegrees: 38f, elevationDegrees: 26f);
        // Pull back a touch and aim slightly above centre so the horizon / sky
        // reads instead of just the ground under the camera.
        var b = world.Bounds;
        var centre = RuntimeWorldScene.ToScene(
            (b.Min.X + b.Max.X) * 0.5, (b.Min.Y + b.Max.Y) * 0.5, (b.Min.Z + b.Max.Z) * 0.5);
        _camera.Position = centre + (_camera.Position - centre) * 1.15f;
        _camera.LookAt(centre + Vector3.Up * (float)((b.Max.Y - b.Min.Y) * 0.15), Vector3.Up);
        _camera.Current = true;
        _activeCamera = _camera;
        GD.Print($"[OBPGame] showcase camera at {_camera.Position}");
    }

    /// <summary>
    /// MobySequence showcase (<c>--anim-solo</c>): park a static camera on the
    /// bounds of the animated mobies alone. Deterministic, no world geometry.
    /// </summary>
    private void FrameAnimatedMobies(RuntimeWorld world)
    {
        var anim = world.AnimatedMeshes;
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        if (anim is { Count: > 0 })
        {
            // Frame the first instance only — a tight shot, not the whole spread.
            string firstInstance = anim[0].Name.Split("_t")[0];
            foreach (var am in anim.Where(a => a.Name.StartsWith(firstInstance)))
            {
                foreach (var frame in am.Frames)
                {
                    for (int i = 0; i + 3 <= frame.Length; i += 3)
                    {
                        minX = System.Math.Min(minX, frame[i]);
                        maxX = System.Math.Max(maxX, frame[i]);
                        minY = System.Math.Min(minY, frame[i + 1]);
                        maxY = System.Math.Max(maxY, frame[i + 1]);
                        minZ = System.Math.Min(minZ, frame[i + 2]);
                        maxZ = System.Math.Max(maxZ, frame[i + 2]);
                    }
                }
            }
        }
        else
        {
            minX = minY = minZ = -1;
            maxX = maxY = maxZ = 1;
        }

        var bounds = new OBP.Core.Math.ObpBounds(
            new OBP.Core.Math.Vec3(minX, minY, minZ),
            new OBP.Core.Math.Vec3(maxX, maxY, maxZ));
        RuntimeWorldScene.FrameCamera(_camera, bounds, azimuthDegrees: 35f, elevationDegrees: 18f);
        var centre = RuntimeWorldScene.ToScene((minX + maxX) * 0.5, (minY + maxY) * 0.5, (minZ + maxZ) * 0.5);
        _camera.Position = centre + (_camera.Position - centre) * 0.55f;
        _camera.LookAt(centre, Vector3.Up);
        _camera.Current = true;
        _activeCamera = _camera;
        GD.Print($"[OBPGame] anim-solo camera at {_camera.Position}, {anim?.Count ?? 0} animated mobies");
    }

    private void SpawnPlayer(RuntimeWorld world)
    {
        var b = world.Bounds;
        RuntimeSpawn? stagedStart = _worldPlayerEntry;

        var preferredStart = stagedStart ?? world.PreferredPlayerStart;
        bool explicitPlayerStart = stagedStart is not null || world.PlayerStart is not null;
        bool nativeStartUsable = preferredStart is { } s
            && (explicitPlayerStart
                || (s.X > b.Min.X && s.X < b.Max.X && s.Z > b.Min.Z && s.Z < b.Max.Z
                    && s.Y > b.Min.Y - 4 && s.Y < b.Max.Y + 40));

        Vector3 spawn;
        float yaw = 0f;
        if (nativeStartUsable && preferredStart is { } sp)
        {
            RuntimeSpawnScenePose pose = RuntimeSpawnSceneAdapter.ToScenePose(sp);
            spawn = pose.Position;
            yaw = pose.SceneYaw;
            string source = stagedStart is not null
                ? "staged source/authored entry"
                : explicitPlayerStart ? "explicit player start" : "native ship point";
            GD.Print($"[OBPGame] spawn from {source} ({sp.X:0},{sp.Y:0},{sp.Z:0})");
        }
        else
        {
            // ship-less levels (Aranos, the Aranos return): drop at the top
            // centre of the world bounds and let the capsule fall to collision.
            spawn = RuntimeWorldScene.ToScene(
                (b.Min.X + b.Max.X) * 0.5,
                b.Max.Y + 6.0,
                (b.Min.Z + b.Max.Z) * 0.5);
            GD.Print("[OBPGame] no in-bounds ship point — spawning at world-bounds centre");
        }

        // MobySequence showcase: stand right next to the first animated moby.
        if (_args.AnimFocus && world.AnimatedMeshes is { Count: > 0 } anim)
        {
            var f0 = anim[0].Frames[0];
            double cx = 0, cy = 0, cz = 0;
            int n = f0.Length / 3;
            for (int i = 0; i < f0.Length; i += 3) { cx += f0[i]; cy += f0[i + 1]; cz += f0[i + 2]; }
            var centre = RuntimeWorldScene.ToScene(cx / n, cy / n, cz / n);
            spawn = centre + new Vector3(0f, 2.5f, 9f);
            var look = centre - spawn;
            yaw = Mathf.Atan2(look.X, look.Z) + Mathf.Pi;
            GD.Print($"[OBPGame] anim-focus spawn near {anim[0].Name} at ({cx / n:0},{cy / n:0},{cz / n:0})");
        }

        if (_worldScenes?.Current?.Resources.CrateDiagnostics.TryGetFocusPose(world, out var crateSpawn, out var crateYaw) == true)
        {
            spawn = crateSpawn;
            yaw = crateYaw;
            GD.Print($"[crate-debug] focus spawn at {spawn}");
        }

        bool scripted = _args.CaptureFrame is not null;

        // When no native player/ship start is usable, deterministic captures
        // face the world centre because the bounds fallback has no authored heading.
        if (scripted && !nativeStartUsable)
        {
            var centre = RuntimeWorldScene.ToScene(
                (b.Min.X + b.Max.X) * 0.5, spawn.Y, (b.Min.Z + b.Max.Z) * 0.5);
            var toCentre = centre - spawn;
            if (toCentre.LengthSquared() > 1f)
            {
                yaw = Mathf.Atan2(toCentre.X, toCentre.Z) + Mathf.Pi;
            }
        }

        var player = new PlayerHost
        {
            Name = "PlayerHost",
            Scripted = scripted,
            ScriptedStill = _args.CrateFocus && !_args.CrateAutoStrike,
            ControlProfile = _playerControlProfile ?? PlayerHostProfiles.Diagnostic,
            // Source/authored placement clearance was staged with the world.
            InitialGroundSnapUpwardReach = _worldEntrySnapReach,
            // OBP deliberately reuses the retail-derived R&C1 controller in
            // all supported trilogy worlds; seed its facing from the authored
            // host spawn heading regardless of source game.
            Rac1CurrentYaw = -yaw,
            Position = spawn,
        };
        _playerRoot.AddChild(player);
        player.SetDebugOutputEnabled(_debugOutputEnabled);
        player.RotateY(yaw);
        _camera.Current = false;
        _activeCamera = player.Camera;
        _player = player;
        PlayerLife.BindPlayer(player);
        player.DevelopmentControls.SaveMovementStateRequested += SaveMovementState;
        player.DevelopmentControls.LoadMovementStateRequested += () => StartMovementStateLoad(MovementStatePath);
        AttachPlayerAvatarVisual(player);
        Rac1World.Arm(player);
        _worldScenes?.Current?.Resources.Rac3Gameplay?.Arm(player, OrdinaryPlayerSession);
    }

    // --- HUD ---------------------------------------------------------------

    private void EnsurePlayerHud()
    {
        if (_playerHud is not null && IsInstanceValid(_playerHud))
        {
            return;
        }

        _playerHud = new PlayerHud { DebugOutputEnabled = _debugOutputEnabled };
        _ui.AddChild(_playerHud);
    }

    private void UpdatePlayerHud()
    {
        if (_playerHud is not null && IsInstanceValid(_playerHud))
        {
            _playerHud.Render(_hudState.Current);
        }
    }

    private void EnsureWorldHud()
    {
        if (_worldHud is not null && IsInstanceValid(_worldHud))
        {
            _worldHud.Visible = _debugOutputEnabled;
            return;
        }

        _worldHud = new Label { Name = "WorldHud", Position = new Vector2(16, 12), Visible = _debugOutputEnabled, MouseFilter = Control.MouseFilterEnum.Ignore };
        _worldHud.AddThemeColorOverride("font_color", new Color(0.85f, 0.92f, 1f));
        _ui.AddChild(_worldHud);
    }

    private void UpdateWorldHud()
    {
        if (_worldHud is null) return;
        _worldHud.Visible = _debugOutputEnabled && _mode == Mode.World && _world is not null;
        if (!_worldHud.Visible)
        {
            _worldHud.Text = string.Empty;
            return;
        }
        if (_world is not { } w || _sceneResult is not { } r)
        {
            return;
        }

        var rac2Entry = w.Game == ObpSourceGame.Rac2 ? Rac2PlanetCatalogue.Find(w.LevelId) : null;
        string gameLabel = w.Game.SourceGame switch
        {
            ObpSourceGame.Rac1 => "Ratchet & Clank",
            ObpSourceGame.Rac2 => "Going Commando",
            ObpSourceGame.Rac3 => "Up Your Arsenal",
            _ => w.Game.ToString(),
        };
        string destinationName = w.PlanetName is { Length: > 0 }
            ? w.DisplayName
            : _activeDestination?.DisplayName ?? "Unknown destination";
        string container = _activeDestination?.NativeContainer ?? rac2Entry?.ContainerFile ?? "(native container unresolved)";
        string pos = "-";
        string ground = "-";
        if (_player is { } p && IsInstanceValid(p))
        {
            var gp = p.GlobalPosition;
            pos = $"{gp.X:0.0} {gp.Y:0.0} {gp.Z:0.0}";
            ground = p.IsOnFloor() ? "grounded" : "air";
        }

        var resources = _worldScenes?.Current?.Resources;
        string crateDebug = resources?.CrateDiagnostics.HudLine(resources.Rac2CrateBolts) ?? string.Empty;
        string rac2Gameplay = resources?.Rac2Gameplay?.HudLine() ?? string.Empty;
        string rac1Gameplay = _worldScenes?.Current?.Resources.Rac1Gameplay?.Hud.Line() ?? string.Empty;
        string rac3Gameplay = resources?.Rac3Gameplay?.HudLine() ?? string.Empty;

        _worldHud.Text =
            $"Game: {gameLabel}   Build: {w.BuildId}\n" +
            $"Destination: {destinationName}\n" +
            $"Level id: {w.LevelId}   Container: {container}   (switch #{_worldSwitches})\n" +
            $"Player XYZ: {pos}   {ground}\n" +
            $"Render tris: {r.Triangles:N0}   Collision tris: {r.CollisionTriangles:N0}\n" +
            $"TIE meshes: {r.TieInstances}   Shrub meshes: {r.ShrubInstances}   Moby meshes: {r.MobyInstances}" +
            (r.AnimatedMobies > 0 ? $"   Animated mobies: {r.AnimatedMobies}" : "") +
            (r.DynamicObjects > 0 ? $"   Dynamic objects: {r.DynamicObjects}" : "") +
            $"\n{_overlay?.StatusLine() ?? "overlays: off"}   (F1 isolate · F2 tint · F3 collision · F4 bounds · F5 lights · F6 sky · F7 clear)" +
            (_worldHost.AudioDiagnostics.Count > 0 ? $"\n{_worldHost.AudioStatusLine}" : "") +
            (string.IsNullOrEmpty(crateDebug) ? "" : $"\n{crateDebug}") +
            (string.IsNullOrEmpty(rac2Gameplay) ? "" : $"\n{rac2Gameplay}") +
            (string.IsNullOrEmpty(rac1Gameplay) ? "" : $"\n{rac1Gameplay}") +
            (GetRac1MetropolisTaxiHudLine() is { Length: > 0 } taxiHint ? $"\n{taxiHint}" : "") +
            (string.IsNullOrEmpty(rac3Gameplay) ? "" : $"\n{rac3Gameplay}");
    }

    // --- smoke / plain scenes -------------------------------------------

    private void EnsurePlainEnvironment()
    {
        if (GetNodeOrNull("PlainEnvironment") is not null)
        {
            return;
        }

        AddChild(new WorldEnvironment
        {
            Name = "PlainEnvironment",
            Environment = new global::Godot.Environment
            {
                BackgroundMode = global::Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.04f, 0.05f, 0.08f),
                AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(1, 1, 1),
                AmbientLightEnergy = 1.0f,
            },
        });
    }

    // Temporary composition for retained Phase 1 callers. #340/#343 remove the nested pause entrypoints;
    // #346/#347/#348 move these bindings to their source/navigation/pause owners.

    private MovementDiagnosticHarness CreateMovementDiagnosticHarness()
    {
        var clock = new GodotDiagnosticFrameClock(this);
        var ports = new MovementScenarioPorts(new GodotMovementPlayer(() => _player, () => _world),
            new GodotMovementAnimation(new(() => _avatarPresentation.View, () => _avatarPresentation.Controller, () => _avatarPresentation.Sink,
                CurrentPlayerAvatarSourceSequence, PlayerAvatarPresentationIsSynchronized, () => _avatarPresentation.Avatar?.AnimationBounds.Height ?? double.NaN)),
            new GodotMovementGeometry(this), new GodotMovementWorld(() => _world,
                () => _sceneResult?.CollisionBodies ?? 0, () => _rac1NovalisWater is not null),
            new GodotDiagnosticInputActions(), new GodotMovementPhysicalInput(), clock, message => GD.Print(message), message => GD.PrintErr(message));
        var water = new GodotMovementWater(() => [.. Rac1World.Enemies.Roster.Values.Select(enemy => ((Node3D)enemy.Node.Root, (Node3D)enemy.Body))],
            position => SampleRac1NovalisWaterHeight(GodotMovementPlayer.Engine(position)),
            () => _playerHud?.FindChild("Air", true, false) is Control air ? air.Visible : null, _rac1Nanotech.Probe);
        var environment = new DiagnosticEnvironmentalDeathFixture(() => new(_rac1Nanotech.EnvironmentalDeathGeneration,
            _rac1Nanotech.EnvironmentalRestartGeneration, _rac1Nanotech.Probe(), Rac1World.Context.LastEnvironmentalDeathBoundary,
            GodotMovementPlayer.Copy(Rac1World.Presentation.LastEnvironmentalDeathPosition), Rac1World.Context.LastEnvironmentalDeathContactSeparation,
            MeasureRac1ContactSeparation(), _rac1Campaign.Session.LevelCheckpoint?.AuthoredClass0.Yaw));
        var equipment = new DiagnosticControllerEquipmentFixture(() =>
        {
            var player = _player ?? throw new DiagnosticUnavailableException("The controller fixture player is unavailable.");
            return new(_rac1Weapons.Equipped, _rac1Weapons.FirstRangedAmmo,
                _rac1HeldItem.NativeItemId, _avatarPresentation.WrenchView?.Visible == true, _avatarPresentation.GloveView?.Visible == true,
                player.Rac1WrenchFrame.Active, player.Rac1AudioMovementAction, CurrentWeaponKey, [.. _persistentPlayer.Equipment.WeaponOrder()]);
        },
            OnRac1WeaponSelectionRequested, OnRac1WeaponCycleRequested);
        return new(ports, water, environment, equipment, _activeDestination,
            [.. Sources.Setup.Library.Attached.Values.Select(source => new DiagnosticSourceRequirement(source.Game, source.Identity.BuildId))],
            new DiagnosticScenarioRunner(new GodotDiagnosticSources(() => [.. Sources.Setup.Library.Attached.Values.Select(source =>
                new DiagnosticSourceSnapshot(source.Game, source.Identity.BuildId, true))]), clock), _hostOperations, _args.MovementCase);
    }
    internal Task CheckControllerWeaponInputAsync() => CreateMovementDiagnosticHarness().RunControllerAsync(_hostOperations.Token);
    private static void SetSyntheticNativeHeading(PlayerHost player, double yaw, double magnitude = 1) => GodotMovementCompatibility.Heading(player, yaw, magnitude);
    private static void AddSyntheticTraversalMesh(Node3D parent, string name, int material, params (Vector3 A, Vector3 B, Vector3 C)[] triangles) =>
        GodotRac1DiagnosticSurface.Add(parent, name, material, triangles);
    private static void SetAnalogueSmokeInput(float right, float forward) => GodotMovementCompatibility.Analogue(right, forward);
    private static void SetRac1SmokeConditionedInput(double right, double forward) => GodotMovementCompatibility.Conditioned(right, forward);
    private static void ClearMovementSmokeInput() => GodotMovementCompatibility.Clear();


    private FusionWeaponDiagnosticHarness CreateFusionWeaponDiagnosticHarness()
    {
        var weapons = CreateWeaponDiagnosticHarness();
        var fixture = new GodotFusionWeaponDiagnosticFixture(this, new()
        {
            Status = () => new(FusionWeapons.Session is not null, FusionWeapons.Sources.Assets is not null, FusionWeapons.Sources.Assets?.Problems.Count ?? -1, FusionWeapons.Sources.Preparation is not null,
                FusionWeapons.Sources.Failure, _frame, FusionWeapons.TestActive, FusionWeapons.Configuration?.LoadoutId, _captureReplay?.Recorder?.WaitingForEntry == true, _captureReplay?.Recorder is not null),
            Session = () => new(FusionWeapons.Session?.Selected, FusionWeapons.Session?.Hand.Equipment, _rac1HeldItem.Hand.WrenchHeld, FusionWeapons.Session?.LancerSession.Ammo ?? 0,
                FusionWeapons.Session?.CannonSession.Ammo ?? 0, FusionWeapons.AcceptedHits, FusionWeapons.AudioStarted, FusionWeapons.BeamVisible, FusionWeapons.Session?.CannonFrame?.BeamUpdatesRemaining ?? 0,
                FusionWeapons.LastDamage is { } damage ? new(damage.Shot.Weapon, damage.SourceDamage, damage.Kind) : null,
                _player?.Rac1WrenchFrame.Active == true, FusionWeapons.Session is not null && ReferenceEquals(FusionWeapons.Session.Rac1Inventory, _rac1Campaign.Session.Weapons),
                ReferenceEquals(_rac1Weapons, _rac1Campaign.Session.Weapons)),
            Transients = () => new(FusionWeapons.Projectiles.Count, FusionWeapons.Session?.LancerSession.ActiveShots.Count ?? 0, FusionWeapons.EffectCount, FusionWeapons.PendingAudioCount, FusionWeapons.AudioPlayerCount),
            Menu = () => new(QuickSelectOpen, _quickSelect is { } menu ? [.. menu.Entries.Select(entry => entry.Weapon)] : [], _quickSelect?.SelectedWeapon),
            NativeSession = () => FusionWeapons.Session,
            Assets = () => FusionWeapons.Sources.Assets,
            ReplaceAssets = assets => FusionWeapons.Sources.Assets = assets,
            Preparation = () => FusionWeapons.Sources.Preparation,
            Sources = () => Sources.Setup.Library,
            ReplaceSources = Sources.Setup.ReplaceDiagnosticLibrary,
            Views = () => [.. FusionWeapons.HeldViews],
            Animation = () => _avatarPresentation.Controller,
            Avatar = () => _avatarPresentation.Avatar,
            OriginalInventory = () => System.Text.Json.JsonSerializer.Serialize((FusionWeapons.OriginalInventory ?? throw new DiagnosticUnavailableException("The isolated original campaign is unavailable.")).CapturePersistentState()),
            CurrentInventory = () => System.Text.Json.JsonSerializer.Serialize(_rac1Weapons.CapturePersistentState()),
            Prepare = () => FusionWeapons.PrepareSources(force: true),
            Reset = () => FusionWeapons.ResetWorld(),
            Clear = () => FusionWeapons.ClearTransients(),
            Reenter = () => { _args = _args with { FusionTest = false }; return OnDestinationChosen(_activeDestination!); },
            ShowDestinations = ShowDestinationSelector,
            Camera = () => _activeCamera,
            Player = () => _player,
            Forward = () => GodotMovementPlayer.Copy(EquipmentForward),
            SourceSequence = CurrentPlayerAvatarSourceSequence,
            Held = () => _rac1HeldItem.Hand.Equipment,
        }, new GodotDiagnosticFrameClock(this));
        return new(weapons, fixture,
            [.. TrilogySourceDefinitions.All.Select(source => new DiagnosticSourceRequirement(source.Game, source.Authority.BuildId))],
            new DiagnosticScenarioRunner(new GodotDiagnosticSources(() => [.. Sources.Setup.Library.Attached.Values.Select(source => new DiagnosticSourceSnapshot(source.Game, source.Identity.BuildId, true))]),
                new GodotDiagnosticFrameClock(this)), _hostOperations, (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code), message => GD.PrintErr(message));
    }

    private Rac1CampaignDiagnosticHarness CreateRac1CampaignDiagnosticHarness()
    {
        var movement = CreateMovementDiagnosticHarness();
        var facts = new Rac1CampaignDiagnosticFacts(() =>
        {
            var campaign = _rac1Campaign.Session.Campaign; var travel = _rac1Campaign.Session.Travel;
            return new(campaign.CurrentLevel, travel.SelectedDestination, [.. campaign.GalacticMap.Take(campaign.AdmittedDestinationCount)], [.. campaign.LevelStates], travel.TransitionActive)
            { NovalisAvailable = campaign.CanSelectDestination(1), AridiaAvailable = campaign.CanSelectDestination(2) };
        }, () =>
        {
            var state = _rac1Nanotech.Probe();
            return new(state.Nanotech, state.IsDead, state.HasRecoveredEnvironmentalRespawn, _rac1Nanotech.EnvironmentalDeathGeneration);
        }, () => _rac1Missions?.Probe().SingleOrDefault(actor => actor.Key.NativeClassId == 774)?.Retired == true);
        var actions = new Rac1CampaignDiagnosticActions(ApplyRac1CampaignProgressionEvent, TravelRac1CampaignTo, () => _rac1Campaign.Session.Travel.OpenPlanetMap(),
            level => OpenDestinationFromBootstrap($"rac1:LEVEL{level}"), TickRac1CampaignSaves, FlushRac1CampaignSaves);
        var checkpoints = new GodotRac1CampaignCheckpointFixture(() =>
        {
            var checkpoint = _rac1Campaign.Session.LevelCheckpoint ?? throw new DiagnosticUnavailableException("R&C1 smoke checkpoint session is missing.");
            return new(checkpoint.NativeLevelId, checkpoint.AuthoredClass0, _world?.PlayerStart, checkpoint.ActiveCheckpoint is not null, checkpoint.ResolveEnvironmentalRestart().Kind);
        }, activation => (_rac1Campaign.Session.LevelCheckpoint ?? throw new DiagnosticUnavailableException("The checkpoint session is unavailable.")).Activate(activation),
        () =>
        {
            var player = _player ?? throw new DiagnosticUnavailableException("The checkpoint fixture player is unavailable.");
            var dead = _rac1Nanotech.ApplyEnvironmentalDeathReset(); player.Rac1GameplayState = dead;
            return new(dead.Nanotech, dead.IsDead, dead.HasRecoveredEnvironmentalRespawn, _rac1Nanotech.EnvironmentalDeathGeneration);
        }, () => TryCompleteRac1EnvironmentalRestart(automatic: false));
        var save = new Rac1CampaignSaveDiagnostics(() => new(ObpSourceGame.Rac1, Sources.Setup.Library.Get(ObpSourceGame.Rac1)?.Identity.BuildId,
            !string.IsNullOrWhiteSpace(_args.Rac1CampaignSavePath), OrdinaryPlayerSession, _rac1CampaignPersistence.Enabled, _rac1SaveBatcher is not null,
            _rac1CampaignRestoreWarning is not null, _rac1CampaignRestoreKind), () => _args.Rac1CampaignSavePath, () => _rac1CampaignPersistence.SavePath);
        return new(movement.Ports, new(facts, actions, checkpoints, save, new GodotRac1MissionDiagnosticUi(() => _ui)));
    }

    private Rac1LootDiagnosticHarness CreateRac1LootDiagnosticHarness()
    {
        var movement = CreateMovementDiagnosticHarness(); var worlds = new GodotDiagnosticWorldObservations(WorldScenes);
        var targets = new GodotRac1WeaponTargetFixture(new(() => [.. Rac1World.Presentation.CrateNodes], () => [.. Rac1World.Enemies.Roster.Values], Rac1World.Enemies.IsRac1GameplayActive,
            (node, position) => GodotRac1CrateStaging.Place(node, position, _rac1FallingCrates),
            id => Rac1World.Enemies.Class1440?.Get(id).Health ?? throw new DiagnosticUnavailableException("The robot health source is unavailable.")), worlds);
        var loot = new GodotRac1LootDiagnosticFixture(new()
        {
            Read = () => new(ObpSourceGame.Rac1, _rac1BoltWallet.Balance, Rac1HudProjection.Capture(_rac1Nanotech, _rac1Weapons, _rac1BoltWallet).Bolts?.Balance,
                _rac1PickupSoundCount, _rac1Nanotech.Probe().Nanotech, _rac1Weapons.FirstRangedAmmo, Rac1World.Context.BoltCrates.OutstandingPickupCount,
                [.. _rac1Debris.Select(piece => piece.Motion.NativeClassId)],
                [.. _rac1PickupMotion.Select(pair => new Rac1LootMotionDiagnostic(pair.Key, pair.Value.NativeClassId, pair.Value.NativeState, pair.Value.Position,
                    GodotMovementPlayer.Copy(FromEffectNative(pair.Value.Position))))],
                [.. _rac1AmmoPickups.Select(pickup => new Rac1AmmoPickupDiagnostic(new(ObpSourceGame.Rac1, pickup.Motion.Definition.Item), pickup.Motion.Definition.Amount,
                    pickup.Motion.Position, pickup.Motion.Velocity, pickup.Motion.DelayTicks, _rac1Weapons.GetAmmo(pickup.Motion.Definition.Item)))],
                [.. _rac1NanotechClouds.Select(cloud => cloud.Motion.Attracting)]),
            OriginalPickupMeshes = () => Rac1World.Presentation.PickupNodes.Values.All(node => GodotWeaponModelFacts.Read(node).OriginalMeshes),
            Pending = () => _rac1PendingPlacedBolts.Count,
            Placed = () => Rac1World.Context.PlacedBolts,
            PlacedNodes = () => _rac1PlacedBoltNodes,
            Motions = () => _rac1PickupMotion,
            PickupNodes = () => Rac1World.Presentation.PickupNodes,
            Random = () => Rac1EffectRandom,
            ToNative = ToEffectNative,
            FromNative = FromEffectNative,
            Orientation = FromEffectOrientation,
            CrateBody = id => _rac1CrateBodies.ContainsKey(id),
            Strike = node => TryStrikeRac1Crate(node),
            Damage = () => _rac1Nanotech.ApplyDamage(Rac1DamageRuntime.FromClass749Attack(new(749, 149), new(34, 1))),
        }, targets, worlds);
        return new(movement.Ports, new(loot, targets, movement.Equipment, new GodotRac1LootDiagnosticCapture(this,
            () => _world?.LevelId ?? throw new DiagnosticUnavailableException("The loot capture world is unavailable."), () => _ui, () => _player, new GodotDiagnosticFrameClock(this))));
    }

    private WeaponDiagnosticHarness CreateWeaponDiagnosticHarness()
    {
        var movement = CreateMovementDiagnosticHarness();
        var worlds = new GodotDiagnosticWorldObservations(WorldScenes);
        var targets = new GodotRac1WeaponTargetFixture(new(() => [.. Rac1World.Presentation.CrateNodes], () => [.. Rac1World.Enemies.Roster.Values],
            Rac1World.Enemies.IsRac1GameplayActive, (node, position) => GodotRac1CrateStaging.Place(node, position, _rac1FallingCrates),
            id => Rac1World.Enemies.Class1440?.Get(id).Health ?? throw new DiagnosticUnavailableException("The robot health source is unavailable.")), worlds);
        var wrench = new GodotRac1WrenchDiagnosticFacts(new(() => _player, () => _avatarPresentation.WrenchView, () => _avatarPresentation.Avatar,
            () => _avatarPresentation.Controller, () => _rac1WrenchHostGeneration, () => _rac1WrenchThrow.Active,
            WrenchWorldPosition, () => _avatarPresentation.GloveView?.Visible == true));
        var capture = new GodotWeaponDiagnosticCapture(this, () => _player, () => _sceneResult?.Root, () => _ui, new GodotDiagnosticFrameClock(this));
        var bomb = new GodotRac1BombAcceptanceFixture(new(
            () =>
            {
                var glove = _avatarPresentation.GloveView; var staged = _avatarPresentation.StagedBombView;
                bool glovePresent = glove is not null && IsInstanceValid(glove), stagedPresent = staged is not null && IsInstanceValid(staged);
                return new(glovePresent && glove!.Visible, stagedPresent && staged!.Visible,
                    _avatarPresentation.WrenchView is { } view && IsInstanceValid(view) && view.Visible,
                    _avatarPresentation.GloveAsset?.VertexCount ?? 0, _avatarPresentation.GloveAsset?.TriangleCount ?? 0,
                    glovePresent ? GodotMovementPlayer.Copy(glove!.BombLocal) : System.Numerics.Vector3.Zero,
                    stagedPresent ? GodotMovementPlayer.Copy(staged!.Position) : System.Numerics.Vector3.Zero,
                    glovePresent && stagedPresent && staged!.Position.IsEqualApprox(glove!.BombLocal));
            },
            () => new(_rac1BombFragments.Count, _rac1BombBursts.Count, _rac1Particles?.Count ?? 0, _rac1BombImpactCount,
                _rac1Particles is { } particles ? [.. particles.Live.Select(particle => particle.Type)] : []),
            () =>
            {
                var shot = Rac1World.Presentation.Projectiles.Single();
                bool originalModel = GodotWeaponModelFacts.Read(shot.Value.Node).OriginalMeshes;
                return new GodotRac1BombAcceptanceWitness(new(worlds), () => new(shot.Key, FusionWeaponSession.BombGlove,
                    shot.Value.Motion.Position, shot.Value.Motion.Velocity, shot.Value.Motion.NativeState, shot.Value.Motion.Flying, originalModel));
            }, BombHasLineOfSight, ProbeBombWorldContact,
            amount => _rac1Weapons.TryConsumeAmmo((int)Rac1WeaponId.FirstRanged, amount), () => _player));
        var transients = new GodotWeaponDiagnosticTransientFixture(worlds, () => DiagnosticCleanup.Run("Weapon fixture transient cleanup",
            () => { foreach (var (id, projectile) in Rac1World.Presentation.Projectiles.ToArray()) RetireBomb(id, projectile); },
            ResetRac1BombPresentation, ResetRac1AdditionalWeapons, () => FusionWeapons.ClearTransients()));
        async Task<bool> LoadWeaponStateAsync(string path, CancellationToken token) => await LoadMovementStateAsync(path, cancellationToken: token) is not null;
        var reproduction = new GodotWeaponMovementStateFixture(() => _movementStateLoading, () => CaptureMovementState(), LoadWeaponStateAsync,
            name => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "captures", "repros", name)), transients);
        return new(movement.Ports, wrench, new(movement.Equipment, targets, CreateRac1EncounterDiagnosticHarness().Combat, capture, bomb, reproduction,
            CreateRac1AdditionalWeaponDiagnosticFixture(worlds), CreateRac1ArsenalDiagnosticFixture(worlds, targets)),
            transients);
    }

    private GodotRac1AdditionalWeaponDiagnosticFixture CreateRac1AdditionalWeaponDiagnosticFixture(IDiagnosticWorldObservations worlds) => new(new(
        () => new(_persistentPlayer.WeaponPreview is not null, _rac1Campaign.Session.Weapons.Owns(Rac1WeaponId.Blaster), _rac1AdditionalWeapons?.Cargo.Count ?? 0,
            _rac1EquipmentUseTicks, _rac1WeaponShotsFired, _rac1PyroEmitting, _rac1WeaponLoop, _hudState.Current.CurrentWeapon,
            GodotMovementPlayer.Copy(EquipmentForward), _player?.Rac1RemoteControlActive == true, _rac1MissileCamera is not null,
            _rac1MissileCamera?.Current == true, _player?.Camera.Current == true, _rac1ParticleView?.RenderedCount ?? 0, _rac1ParticleView?.DrawCalls ?? 0),
        weapon => _rac1EquipmentViews.GetValueOrDefault(weapon), weapon => EquipmentMuzzle(weapon), item => _rac1Weapons.GetAmmo(item),
        (nativeClass, sound) => _rac1PickupAssets?.Sounds?.TryGetValue((nativeClass, sound), out var voice) == true && voice.Clip.InterleavedPcm16.Any(sample => sample != 0),
        () => [.. _rac1WeaponSoundTrace.Select(cue => new Rac1WeaponAudioDiagnostic(ObpSourceGame.Rac1, cue.Tick, cue.Class, cue.Sound, cue.Loop))],
        () => [.. _rac1EquipmentShots.Select(shot => new GodotWeaponDiagnosticWitness<Rac1EquipmentShotDiagnostic>(new(worlds), () => new(
            new(ObpSourceGame.Rac1, (int)shot.Weapon), shot.Id, GodotMovementPlayer.Copy(shot.Position), shot.Grounded, GodotWeaponModelFacts.Read(shot.Node))))],
        () => [.. _rac1DoomAgents.Select(agent => new GodotWeaponDiagnosticWitness<Rac1DoomAgentDiagnostic>(new(worlds), () => new(
            new(ObpSourceGame.Rac1, (int)Rac1WeaponId.GloveOfDoom), agent.Id, GodotMovementPlayer.Copy(agent.Body.GlobalPosition), agent.Motion.Wander,
            GodotWeaponModelFacts.Read(agent.View))))], () => _persistentPlayer.WeaponPreview is not null, SetRac1WeaponPreview));

    private GodotRac1ArsenalDiagnosticFixture CreateRac1ArsenalDiagnosticFixture(IDiagnosticWorldObservations worlds, GodotRac1WeaponTargetFixture targets) => new(new(
        () => new(_rac1RynoSalvo.Remaining, _rac1ArsenalId, _rac1Drones.Count, _rac1ChickenCargo.Count,
            [.. _rac1ArsenalBeams.Select(beam => beam.Visible)], [.. _rac1ArsenalBeams.Select(beam => beam.MaterialOverride is ShaderMaterial)]),
        () => [.. _rac1ArsenalMissiles.Select(missile => new GodotWeaponDiagnosticWitness<Rac1ArsenalMissileDiagnostic>(new(worlds), () => new(
            new(ObpSourceGame.Rac1, (int)missile.Weapon), missile.Id, GodotWeaponModelFacts.Read(missile.View))))],
        () => [.. _rac1Decoys.Select(decoy => new GodotRac1DecoyDiagnosticFixture(new(worlds), () => new(new(ObpSourceGame.Rac1, (int)Rac1WeaponId.DecoyGlove),
            GodotWeaponModelFacts.Read(decoy.View), decoy.Grounded, decoy.RetiringTicks), () => decoy.GroundTicks = Rac1ArsenalRules.DecoyLifetime - 1))],
        () => [.. _rac1Chickens.Values.Select(chicken => new Rac1ChickenDiagnostic(targets.Key(chicken.Enemy.Node), Rac1World.Enemies.IsRac1GameplayActive(chicken.Enemy.Node),
            chicken.Enemy.RewardEmitted, GodotWeaponModelFacts.Read(chicken.View)))],
        () =>
        {
            foreach (var drone in _rac1Drones.Where(drone => IsInstanceValid(drone.View))) drone.View.QueueFree();
            _rac1Drones.Clear();
        },
        () => _rac1WeaponWorldTick = 0,
        (actor, player) =>
        {
            targets.Read(actor); return GodotMovementPlayer.Copy(ArsenalEnemyGoal(
            Rac1World.Enemies.Roster.TryGetValue(actor.Target.InstanceIndex, out var enemy) ? enemy : throw new DiagnosticUnavailableException("The arsenal fixture enemy is unavailable."),
            GodotMovementPlayer.Engine(player)));
        }));

    private Rac1EncounterDiagnosticHarness CreateRac1EncounterDiagnosticHarness()
    {
        var actors = new GodotRac1EnemyDiagnosticFixture(new(() => [.. Rac1World.Enemies.Roster.Values],
            () => Rac1World.Enemies.Class1440, () => Rac1World.Enemies.Peckbots, () => Rac1World.Enemies.Birdbots, () => Rac1World.Context.Hostiles,
            () => new(Rac1World.Enemies.Roster.Count, Rac1World.Enemies.Class1440?.RegisteredCount ?? 0, Rac1World.Enemies.Peckbots?.RegisteredCount ?? 0,
                Rac1World.Enemies.Birdbots?.RegisteredCount ?? 0, Rac1World.Enemies.BirdbotShotCount, _rac1Nanotech.Probe().Nanotech,
                _worldSwitches, (_rac1CombatRetry ?? PlayerLife.RetryPanel)?.Visible == true), () => _player, node => TryStrikeRac1Hostile(node),
            Rac1World.Enemies.TryGroundRac1Enemy, Rac1EnemyHost.EnemyForward, Rac1World.Enemies.EnemySocket, Rac1World.Enemies.EnemyRayTouchesPlayer,
            id => Rac1World.Enemies.HasBirdbotShot(id)), new GodotDiagnosticWorldObservations(WorldScenes));
        var movement = CreateMovementDiagnosticHarness();
        var loot = new GodotRac1EncounterLoot(() => new(_rac1BoltWallet.Balance, Rac1World.Context.BoltPickups.OutstandingCount,
            [.. _rac1PickupMotion.Select(pair => new Rac1DiagnosticPickup(pair.Key, pair.Value.NativeClassId,
                pair.Value.Position, pair.Value.NativeState, pair.Value.CollectionStep))],
            [.. _rac1Debris.Select(piece => piece.Motion.NativeClassId)]));
        var lifecycle = new GodotRac1EncounterLifecycle(() => _rac1Nanotech.Probe(), Rac1World.Enemies.ApplyRac1EnemyPlayerDamage, () => _rac1CombatRetry ?? PlayerLife.RetryPanel);
        var approach = new GodotRac1WrenchApproachFixture(() => _player, _persistentPlayer.Wrench.ResolveFirstSwingFacing, Rac1WorldPresentation.WrenchHostRootHeight);
        var combat = new Rac1CombatScenarioPorts(actors, actors, CreateRac1CombatDiagnosticFixture(actors), approach, movement.Equipment,
            new DiagnosticNavigationActions(destination => { OpenDestinationFromBootstrap(destination); return _world is not null && _currentDestinationId == destination; }),
            CreateUiDiagnosticHarness(), movement.Environment, lifecycle);
        return new(movement.Ports, loot,
            new GodotRac1EnemyDiagnosticCapture(this, () => _ui, () => _player, new GodotDiagnosticFrameClock(this)),
            CreateRac1AridiaDiagnosticFixture(actors), CreateRac1FrogDiagnosticFixture(actors), combat);
    }

    private GodotRac1CombatDiagnosticFixture CreateRac1CombatDiagnosticFixture(GodotRac1EnemyDiagnosticFixture actors) => new(actors,
        new GodotDiagnosticWorldObservations(WorldScenes), new()
        {
            Crates = () => [.. Rac1World.Presentation.CrateNodes],
            Hostiles = () => [.. Rac1World.Presentation.HostileNodes.Values],
            World = () => _world,
            Player = () => _player,
            Opening = () => new(_args.Rac1CampaignPersistenceRequested, _rac1CampaignPersistence.Enabled, _rac1CampaignRestoreKind,
                _rac1Campaign.Session.Campaign.CurrentLevel, _rac1Campaign.Session.Campaign.AdmittedDestinationCount, _rac1Weapons.OwnsFirstRanged),
            Population = () => new(_world?.DynamicObjects?.Count(source => source.NativeClassId == Rac1Class749Hostile.NativeClassId) ?? 0,
                Rac1World.Presentation.Class749Nodes.Count(node => IsInstanceValid(node.Root) && node.Root.Visible), Rac1World.Presentation.HostileNodes.Count, Rac1World.Context.Hostiles.RegisteredCount),
            Presentation = () =>
            {
                var wrench = _avatarPresentation.WrenchView; bool present = wrench is not null && IsInstanceValid(wrench);
                return new(_hudState.Current.CurrentWeapon, present, present && wrench!.Visible,
                    present && _avatarPresentation.View is not null && wrench!.GetParent() == _avatarPresentation.View);
            },
            Loot = () => new(Rac1World.Context.BoltCrates.DestroyedCrateCount, Rac1World.Context.BoltCrates.CollectedBolts,
                [.. Rac1World.Presentation.PickupNodes.Select(pair => new Rac1CombatPickupDiagnostic(pair.Key, IsInstanceValid(pair.Value),
                    IsInstanceValid(pair.Value) ? GodotMovementPlayer.Copy(pair.Value.GlobalPosition) : System.Numerics.Vector3.Zero, Rac1World.Context.BoltCrates.OwnsPickup(pair.Key)))]),
            Bombs = () => new(Rac1World.Context.BombFireRequested, [.. Rac1World.Presentation.Projectiles.Keys],
                Rac1World.Context.LastBombContactResolution is { } contact ? new(contact.ProjectileId, [.. contact.DamageResults]) : null),
            BombWitness = () =>
            {
                var shot = Rac1World.Presentation.Projectiles.FirstOrDefault();
                return shot.Value is null ? null : new GodotRac1BombDiagnosticWitness(() => new(shot.Key,
                    new(ObpSourceGame.Rac1, (int)Rac1WeaponId.FirstRanged), shot.Value.Motion.Position, shot.Value.Motion.NativeState));
            },
            Active = Rac1World.Enemies.IsRac1GameplayActive,
            Forward = Rac1EnemyHost.EnemyForward,
            Presence = Rac1World.Enemies.SetRac1HostedPresence,
            PrimaryAttack = OnRac1PrimaryAttackRequested,
            Damage = () => Rac1World.Context.DamageTransport,
        });

    private GodotRac1AridiaDiagnosticFixture CreateRac1AridiaDiagnosticFixture(GodotRac1EnemyDiagnosticFixture actors)
    {
        return new(actors, new(() => Rac1World.Context.MobyRuntime, () => _rac1AridiaEnemies, session => _rac1AridiaEnemies = session,
            () => _rac1AridiaClipVolumes.Count, index => _rac1AridiaClipVolumes[index].Centre,
            (index, point) => _rac1AridiaClipVolumes[index].Contains(point),
            id => _rac1ConstructorbotNativeWaypoints.TryGetValue(id, out var point) ? point : null,
            id => _rac1ConstructorbotRoamingTargets.TryGetValue(id, out var point) ? point : null,
            id => _rac1ConstructorbotRoamExitFlags.TryGetValue(id, out var flags) ? flags : null,
            id => _rac1ConstructorbotRoamingTargets.Remove(id),
            id => _rac1ConstructorbotNativeLiftCounts.GetValueOrDefault(id),
            id => _rac1ConstructorbotSweepProbeCounts.GetValueOrDefault(id),
            id => _rac1ConstructorbotStateThreeVerticalSteps.GetValueOrDefault(id), Rac1EnemyHost.AridiaHostUpdateRadius,
            ProbeRac1ConstructorbotStateThreeFloor, ProbeRac1AridiaWalkerGroundLift, ProbeRac1AridiaWalkerSupport,
            ProbeRac1AridiaWalkerSpherePushouts, MoveRac1AridiaWalkerContact, Rac1EnemyHost.SelectRac1EnemySequence, ActivateNearbyRac1AridiaShark));
    }

    private GodotRac1FrogDiagnosticFixture CreateRac1FrogDiagnosticFixture(GodotRac1EnemyDiagnosticFixture actors) => new(actors,
        new GodotRac1DiagnosticWorldScope(new GodotDiagnosticWorldObservations(WorldScenes)), new()
        {
            Frogs = () => _rac1Frogs,
            Shuttle = () => _rac1FrogShuttle,
            Waves = () => _rac1FrogWaves,
            WaveControllers = () => [.. _rac1FrogWaveNodes.Keys],
            Carriers = () => System.Collections.Immutable.ImmutableDictionary.CreateRange(_rac1FrogCarrierChildren),
            HasPickupModel = nativeClass => _rac1PickupAssets?.Models.ContainsKey(nativeClass) == true,
            Counts = () => (Rac1World.Presentation.CrateNodes.Count, _rac1PickupMotion.Count, _rac1Debris.Count),
            TickFrog = TickRac1Frog,
            TickCarrier = TickRac1FrogCarrier,
            MeleeTouchesPlayer = Rac1World.Enemies.EnemyMeleeTouchesPlayer,
            ResolveWrench = ResolveRac1AnimatedWrenchContacts,
            TickShuttle = TickRac1FrogShuttleSwitch,
            ApplyShuttle = ApplyRac1FrogShuttleSelection,
            TickWaves = TickRac1FrogWaves,
            Queen = CreateRac1QueenDiagnosticBindings(),
        });

    private GodotRac1QueenDiagnosticBindings CreateRac1QueenDiagnosticBindings() => new()
    {
        Group = () => _rac1FrogQueen,
        Recovery = () => _rac1QueenRecovery,
        Entrance = () => _rac1QueenEntrance,
        Flight = () => _rac1QueenFlight,
        PendingCamera = () => _rac1QueenPendingCamera,
        Linked = () => _rac1QueenLinkedGate,
        Tuning = () => _rac1QueenGateNative,
        Animation = () => _rac1QueenAnimation,
        Node = () => _rac1QueenNode,
        AuthoredNativeZ = () => _rac1QueenAuthoredNativeZ,
        Parents = () => System.Collections.Immutable.ImmutableDictionary.CreateRange(_rac1QueenLinkedNodes),
        Children = () => System.Collections.Immutable.ImmutableDictionary.CreateRange(_rac1QueenLinkedChildVisuals),
        WorldRoot = () => _sceneResult?.Root,
        Player = () => _player,
        ActiveCamera = () => _activeCamera,
        QueenCamera = () => _rac1QueenScriptedCamera,
        GuidedCamera = () => _rac1MissileCamera,
        DamageSequence = () => Rac1World.Context.DamageTransport.Sequence,
        ForgetWrenchContact = key => _rac1WrenchHitTargets.Remove(key),
        ResolveWrench = ResolveRac1QueenWrenchContacts,
        InvalidateEquipmentGeometry = () => _rac1EquipmentGeometryFrame = ulong.MaxValue,
        GeometryContainsQueen = () => _rac1QueenNode is { } queen && EquipmentGeometry.Any(entry => entry.Node == queen),
        AimTargetAdmitted = () => _rac1QueenNode is { } queen && IsRac1WeaponAimTarget(queen),
        TickFlight = () => TickRac1QueenFlight(),
        TickRecovery = TickRac1QueenRecovery,
        TickLinked = TickRac1QueenLinkedChildren,
        SynchronizeLinked = SynchronizeRac1QueenLinkedParents,
        ObserveFrogEvent = ObserveRac1QueenFrogEvent,
        BeginGuided = BeginRac1GuidedCamera,
        EndGuided = EndRac1GuidedCamera,
        EndQueen = EndRac1QueenScriptedCamera,
    };

    private CaptureDiagnosticHarness CreateCaptureDiagnosticHarness() => CaptureReplay.Diagnostics(() =>
    {
        var clock = new GodotDiagnosticFrameClock(this);
        var recorder = new GodotCaptureRecorderDiagnosticFixture(() => new()
        {
            RecorderPresent = _captureReplay?.Recorder is not null,
            WaitingForEntry = _captureReplay?.Recorder?.WaitingForEntry == true,
            PreparingWorld = _captureReplay?.Recorder?.PreparingWorld == true,
            Finished = _captureReplay?.Recorder?.Finished == true,
            PlayerPresent = _player is not null,
            LevelId = _world?.LevelId,
            Destination = _currentDestinationId,
            Configuration = _playerSession?.Capture().Configuration,
            ProfileJson = _playerSession is null ? "" : System.Text.Json.JsonSerializer.Serialize(OBP.Fusion.ProfilePlayer.Capture(_playerSession.Capture()) with { Generation = 0 }),
            GenuineAvatar = _avatarPresentation.View is not null && _avatarPresentation.Avatar?.Identity == _playerSession?.Capture().Configuration.Avatar,
            NativeProfile = _player?.ControlProfile == PlayerHostProfiles.Native,
            PhysicsTicksPerSecond = Engine.PhysicsTicksPerSecond,
            HudHealth = _hudState.Current.Health is not null,
            HudBolts = _hudState.Current.Bolts is not null,
            HudVisible = _playerHud?.Visible == true,
            QuickSelectOpen = QuickSelectOpen,
            PauseOpen = PauseMenuOpen,
            TreePaused = GetTree().Paused,
            PhysicsTick = _avatarPresentation.Clock?.Tick ?? 0,
            PlayerNode = _player?.GetInstanceId(),
            Shots = FusionWeapons.Session?.ShotsFired ?? 0,
        }, (width, height) => GetWindow().Size = new(width, height), () => _args.RecordPlaytest,
            path => _captureReplay!.Recorder!.RequestBookmark(path), () => _captureReplay!.Recorder!.Save());
        var navigation = new DiagnosticNavigationActions(destination => destination == "obp:fusion-lab" ? EnterFusionLab() :
            TrilogyWorldProviders.Resolve(destination) is { } native && OnDestinationChosen(native));
        var equipment = new CaptureEquipmentActions((slot, item) => AssignPlayerQuickSelect(slot, item).Accepted, item => SelectPlayerEquipment(item).Accepted);
        var reports = new GodotBugReporterDiagnosticFixture(this, () => _bugReporting, () => BugReportModalOpen,
            () => new(GodotMovementPlayer.Copy(_player!.GlobalPosition), _rac1BoltWallet.Balance,
                System.Text.Json.JsonSerializer.Serialize(_rac1Weapons.CapturePersistentState()), QuickSelectOpen, PauseMenuOpen, GetTree().Paused),
            () => _captureReplay?.Recorder as Node);
        var acceptance = new OwnedDiagnosticAcceptanceReporter(() => _currentDestinationId, () => _args.AcceptanceOut,
            () => new(DisplayServer.GetName() == "headless", [.. Sources.Setup.Library.Attached.Values.Select(source => source.Identity)],
                _playerSession?.Capture().Configuration, [.. ReadPlayerEquipment().Select(entry => new AcceptanceEquipment(entry.Definition.Id,
                    entry.Definition.SourceGame, entry.Definition.Implementation, entry.Usability, entry.Reason))]),
            message => GD.PrintErr(message), (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code));
        var captures = ProjectSettings.GlobalizePath("res://../captures");
        var ports = new CaptureDiagnosticPorts(recorder, equipment, reports, CreateMovementStateDiagnosticFixture,
            CreateTrilogyPlayerDiagnosticFixture(), CreateTrilogyActorDiagnosticFixture(), navigation,
            token => CreateFusionWeaponDiagnosticHarness().RunRecordingAsync(token),
            () => CaptureDiagnosticIsolation.Isolated(SharedProfilePath, captures) && CaptureDiagnosticIsolation.Isolated(_bugReporting?.StorageRoot, captures));
        return new(new(() => [.. Sources.Setup.Library.Attached.Values.Select(source => new DiagnosticSourceRequirement(source.Game, source.Identity.BuildId))],
                [.. TrilogySourceDefinitions.All.Select(source => new DiagnosticSourceRequirement(source.Game, source.Authority.BuildId))]),
            ports, CreateMovementDiagnosticHarness().Ports, new GodotCaptureDiagnosticInputFactory(), new GodotCaptureDiagnosticTime(), acceptance,
            new DiagnosticScenarioRunner(new GodotDiagnosticSources(() => [.. Sources.Setup.Library.Attached.Values.Select(source => new DiagnosticSourceSnapshot(source.Game, source.Identity.BuildId, true))]), clock),
            _hostOperations, (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code), message => GD.Print(message), message => GD.PrintErr(message));
    });

    private IMovementStateDiagnosticFixture CreateMovementStateDiagnosticFixture() => new GodotMovementStateDiagnosticFixture(new()
    {
        Player = () => _player,
        Enemies = () => [.. Rac1World.Enemies.Roster.Values.Select(enemy => enemy.Node)],
        Crates = () => [.. Rac1World.Presentation.CrateNodes],
        Active = Rac1World.Enemies.IsRac1GameplayActive,
        Damage = Rac1World.Enemies.ApplyRac1EnemyWeaponDamage,
        Strike = node => TryStrikeRac1Crate(node),
        Read = () => new([.. Rac1World.Enemies.Roster.Values.Select(enemy => new MovementStateEnemyDiagnostic(new(ObpSourceGame.Rac1, enemy.Node.Source.NativeClassId,
                enemy.Node.Source.InstanceIndex), Rac1World.Enemies.IsRac1GameplayActive(enemy.Node), enemy.Node.Root.Visible, enemy.Body.CollisionLayer, enemy.RewardEmitted))],
            [.. Rac1World.Presentation.CrateNodes.Select(node => new MovementStateCrateDiagnostic(new(ObpSourceGame.Rac1, node.Source.NativeClassId, node.Source.InstanceIndex),
                Rac1World.Enemies.IsRac1GameplayActive(node), _rac1CrateBodies.ContainsKey(node.Source.InstanceIndex)))], Rac1World.Context.BoltCrates.DestroyedCrateCount,
            _rac1Weapons.Equipped, _rac1Weapons.FirstRangedAmmo, _rac1BoltWallet.Balance, _rac1PickupMotion.Keys.Any(id => id > 0)),
        Consume = () => { OnRac1WeaponSelectionRequested(Rac1WeaponId.FirstRanged); return _rac1Weapons.TryUseEquipped(); },
        Credit = amount => _rac1BoltWallet.Credit(amount),
        Loading = () => _movementStateLoading,
        Override = () => _movementStatePathOverride,
        SetOverride = path => _movementStatePathOverride = path,
        Path = () => MovementStatePath,
        Load = async (path, frames, token) =>
        {
            var result = await LoadMovementStateAsync(path, recordFrames: frames, cancellationToken: token); token.ThrowIfCancellationRequested();
            if (result is null) throw new InvalidOperationException("Movement state load/replay failed.");
            return [.. result.Select(sample => new MovementStateReplayDiagnostic(sample.Position, sample.ControlHeading))];
        },
        Forward = async (frames, token) => [.. (await RecordForwardMovementReplayAsync(frames, token)).Select(sample => new MovementStateReplayDiagnostic(sample.Position, sample.ControlHeading))],
    });

    private ITrilogyPlayerDiagnosticFixture CreateTrilogyPlayerDiagnosticFixture() => new GodotTrilogyPlayerDiagnosticFixture(this, new()
    {
        Session = () => _playerSession!,
        Player = () => _player,
        Scene = () => _sceneResult!.Root,
        Read = () => new(OrdinaryPlayerSession, _playerNative is not null, _equipmentPresentation.Assets(OrdinaryPlayerSession, _rac1PickupAssets) is not null, _playerAudio?.Ready == true,
            _rac1HeldItem.Hand.WrenchHeld, _avatarPresentation.Clock?.Tick ?? 0, FusionWeapons.Session?.ShotsFired ?? 0, _pauseMenu?.UniversalEnabled == true,
            _pauseMenu?.RetailEnabled == true, _rac1PyroEmitting, _rac1Particles?.Live.Any(particle => particle.Type == 12) == true,
            _rac1ParticleView?.RenderedCount ?? 0, _playerNative?.Life.Capture().Current ?? 0, GodotMovementPlayer.Copy(_player?.GlobalPosition ?? Vector3.Zero),
            _player?.Rac1CurrentYaw ?? 0, FusionWeapons.AcceptedHits, FusionWeapons.Projectiles.Count, FusionWeapons.AudioPlayerCount),
        ResetAudio = () => _playerAudio?.ResetDiagnosticRandom(1440),
        Audio = () => [.. (_playerAudio?.ReadSelected() ?? []).Select(cue => new TrilogyAudioDiagnostic(SourceIdentityWire.FormatSource(cue.Source), cue.Build, cue.Level, cue.Class, cue.Sound, cue.LevelSound, cue.Loop, cue.NativeTick))],
        Trial = token => CreatePlayerDiagnosticHarness().CaptureTrialAsync(token),
        OpenPause = TryOpenPauseMenu,
        PauseSection = section => _pauseMenu!.ShowUniversalSection(section),
        ClosePause = () => TryClosePauseMenu(),
        Equipment = () => [.. ReadPlayerEquipment().Select(entry => new TrilogyEquipmentDiagnostic(entry.Definition.Id, entry.Definition.Item,
            entry.Selectable, entry.Reason, entry.Definition.Item is { } item ? FusionWeapons.Session!.Status(item).Ammo : 0))],
        Respawn = () => _playerNative!.Life.Respawn(),
    });

    private ActorWorldHost CreateActorHost() => new(_hostOperations, new(
        () => Sources.Setup.Library, SourceFileStamp, key => _persistentPlayer.Equipment.Sources.GetValueOrDefault(key.SourceGame),
        (live, total, problems) => _navigation.UpdateLabStatus(live, total, problems),
        PlayerLife.RetireActor,
        PlayerLife.Contains, () => _rac1BombOcclusionFrame = ulong.MaxValue,
        id =>
        {
            Rac1World.Context.BoltPickups.Discard(id); _rac1PickupMotion.Remove(id);
            if (Rac1World.Presentation.PickupNodes.Remove(id, out var node) && IsInstanceValid(node)) node.QueueFree();
        }, () => unchecked((uint)(_args.FixedSeed ?? System.Environment.TickCount))), new(
        new(() => _worldScenes?.Current?.Resources.Rac1Gameplay?.Enemies,
            () => Rac1World.Enemies.Targets.Concat(Rac1World.Presentation.CrateNodes), ApplyRac1AdditionalDamage,
            key => _rac1Captures.RemoveAll(capture => capture.Key == key),
            () => _rac1PickupAssets, () => _equipmentPresentation.SelectedAssets,
            (assets, materials) => { _rac1PickupAssets = assets; _rac1PickupMaterials = materials; }, TickRac1Pickups),
        new(() => _worldScenes?.Current?.Resources.Rac2Gameplay),
        new(() => _worldScenes?.Current?.Resources.Rac3Gameplay)), new(        () => _player!.GetWorld3D().DirectSpaceState, Rac1DamageableRayExclusions, BombHasLineOfSight,
        reason => Rac1World.Context.CombatStatus = reason, () => _player?.Rac1WrenchFrame.Generation,
        () => _playerAudio?.WrenchContact(false)));

    public EquipmentChangeResult ResetPlacedActor(string id) => ActorHost.ResetPlacement(id);

    private ITrilogyActorDiagnosticFixture CreateTrilogyActorDiagnosticFixture() => new GodotTrilogyActorDiagnosticFixture(new()
    {
        Actors = () => ActorHost.Diagnostics,
        ResetEncounter = () => { FusionWeapons.ClearTransients(); _rac1EffectRandom = Rac1PickupRandom.FromSeed(1440); },
        ResetSharedAssets = FusionWeapons.ResetSources,
    });

    private UiDiagnosticHarness CreateUiDiagnosticHarness()
    {
        var worlds = new GodotDiagnosticWorldObservations(WorldScenes);
        var menus = new GodotDiagnosticMenus(this, _worldRoot,
            () => new(_mode == Mode.Title, _mode == Mode.Selector, _mode == Mode.World, _mode == Mode.Picker,
                (Sources.Setup.Access == SourceAccessMode.ExplicitDiagnostic), Sources.HasView, _navigation.Title is not null, _world is not null,
                _currentDestinationId, _profileDocument?.Destination, _profileWriteBlocked, null, false, false),
            () => new([], _player?.VisualRoot.FallbackVisible == true, _sceneResult is not null,
                _player is not null && IsInstanceValid(_player), _avatarPresentation.View is not null && IsInstanceValid(_avatarPresentation.View),
                _player is not null && ReferenceEquals(_activeCamera, _player.Camera) && _player.Camera.Current, _world?.LevelId),
            surface => surface switch
            {
                DiagnosticMenuSurface.Title => _navigation.Title,
                DiagnosticMenuSurface.WorldSelect => _navigation.Selector,
                DiagnosticMenuSurface.Overlay => _ui,
                DiagnosticMenuSurface.World => _sceneResult?.Root,
                DiagnosticMenuSurface.Pause => _pauseMenu,
                _ => null
            },
            () => _navigation.Title, () => _navigation.Selector, () => _pauseMenu,
            ShowTitleScreen, ShowDestinationSelector, TryHandleInteractiveBack, TryOpenPauseMenu, TryClosePauseMenu);
        return new(_args, Sources.Setup.Access != SourceAccessMode.ExplicitDiagnostic,
            [.. TrilogySourceDefinitions.All.Select(source => new DiagnosticSourceRequirement(source.Game, source.Authority.BuildId))], menus,
            new DiagnosticPlayerContinuityAdapter(() => _playerSession?.Capture() ?? throw new DiagnosticUnavailableException("The player session is missing.")),
            new DiagnosticProfileEvidence(() => SharedProfilePath, SaveSharedProfile),
            new DiagnosticSourceRepairFixture(Sources.Setup.Library.Remove, Sources.RequireOrdinary, () => _rac1Campaign.Session),
            new GodotRetailPauseFixture(this, () => _player, () => _pauseMenu, () => PauseMenuOpen, TryOpenPauseMenu, TryClosePauseMenu, () => _rac1Weapons, worlds), worlds,
            new DiagnosticNavigationActions(destination => { OpenDestinationFromBootstrap(destination); return _world is not null && _currentDestinationId == destination; }),
            new DiagnosticScenarioRunner(new GodotDiagnosticSources(() => [.. Sources.Setup.Library.Attached.Values.Select(source => new DiagnosticSourceSnapshot(source.Game, source.Identity.BuildId, true))]),
                new GodotDiagnosticFrameClock(this)), _hostOperations,
            (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code), message => GD.Print(message), message => GD.PrintErr(message));
    }

    private PlayerSessionDiagnosticHarness CreatePlayerDiagnosticHarness()
    {
        var worlds = new GodotDiagnosticWorldObservations(WorldScenes);
        var player = new GodotPersistentPlayerFixture(new(() => OrdinaryPlayerSession, () => _playerSession, () => _playerNative,
            () => FusionWeapons.Session, () => _player, () => _avatarPresentation.View, () => _avatarPresentation.Avatar?.Identity, _playerRoot, _worldRoot,
            () => (FusionWeapons.Projectiles.Count, FusionWeapons.AudioPlayerCount, FusionWeapons.EffectCount), () => _activeDestination is not null, () => _currentDestinationId),
            worlds, FusionWeapons.Select, SetRac1WeaponPreview);
        var sources = new DiagnosticSourceAttachmentFixture(Sources.Setup.Library, RefreshPlayerSourceAvailability);
        var navigation = new DiagnosticNavigationActions(destination => destination == "obp:fusion-lab" ? EnterFusionLab() :
            TrilogyWorldProviders.Resolve(destination) is { } native && OnDestinationChosen(native));
        var menus = CreateUiDiagnosticHarness().Menus;
        var equipment = new DiagnosticPlayerEquipmentFixture(ReadPlayerEquipment, SelectPlayerEquipment, AssignPlayerQuickSelect,
            UnequipPlayerEquipment, SetPlayerGadgetEnabled, () => _playerNative?.GadgetEnabled(OBP.RAC1.Player.Rac1TraversalEquipmentId.HeliPack) == true);
        var ui = new DiagnosticPlayerInterfaceFixture(() => new(_playerHud?.Visible == true, _hudState.Current.Health, _hudState.Current.Bolts,
            _quickSelect is null ? [] : [.. _quickSelect.Entries.Select(entry => entry.Weapon)], _rac1DetectorEquipped),
            () => { using var press = new InputEventAction { Action = ControllerBindings.QuickSelect, Pressed = true }; return TryOpenWeaponQuickSelect(press); },
            () => CloseWeaponQuickSelect(), TryToggleRac1MetalDetector);
        var profile = new DiagnosticSharedProfileFixture(() => new(OrdinaryPlayerSession, SharedProfilePath is not null, _profileWriteBlocked,
            FusionWeapons.Sources.Preparation is not null, _profileDocument?.Destination, _currentDestinationId, _profileDocument?.Sources.Count ?? 0), () => _playerNative);
        var samples = new GodotCapabilitySamples(new(() => _player, () => _avatarPresentation.Clock, () => PlayerLife.ServiceTick,
            () => _avatarPresentation.Controller, () => FusionWeapons.Session?.ShotsFired ?? 0));
        var health = new GodotCapabilityHealthFixture(new(() => _playerNative, () => _player,
            () => _worldScenes?.Current?.Resources.Rac2Gameplay?.ParityNanotech ?? 0,
            () => _hudState.Current.Health, () => [.. (_worldScenes?.Current?.Resources.Rac2Gameplay?.OpeningHostileNodes() ?? [])], PlayerLife.Respawn));
        var audio = new GodotPlayerAudioFixture(this, new(() => _player,
            () => new(_playerAudio?.Ready == true, _world?.LevelId, _playerAudio?.PackSound, _playerAudio?.ThrownWrenchSound, _rac1WeaponLoop is not null,
                _rac1WrenchThrow.Active, Rac1World.Context.BoltCrates.DestroyedCrateCount, _rac1Nanotech.Probe().Nanotech, _rac1Nanotech.Probe().IsDead),
            () => _playerAudio?.ReadVoices() ?? [],
            ClearMovementSmokeInput, SetAnalogueSmokeInput, SetRac1SmokeConditionedInput, TryHandleInteractiveBack,
            OnRac1WeaponSelectionRequested, SetRac1WeaponPreview, () => [.. Rac1World.Presentation.CrateNodes], Rac1World.Enemies.IsRac1GameplayActive,
            (node, position) => GodotRac1CrateStaging.Place(node, position, _rac1FallingCrates), Rac1World.Enemies.ApplyRac1EnemyPlayerDamage));
        var ports = new PlayerSessionDiagnosticPorts(player,
            new GodotPlayerObservations(() => _player, () => _avatarPresentation.Avatar?.Identity,
                () => _avatarPresentation.Clock?.Tick ?? throw new DiagnosticUnavailableException("The native player clock is unavailable."), () => _playerNative?.Life.Capture().Current),
            new GodotPlayerInputFixture(() => _player), navigation, menus, sources,
            new GodotWorldLifecycleFixture(this, WorldScenes, EnterFusionLab,
                () => EnterWorld(DiagnosticWorldFailures.Destination, DiagnosticWorldFailures.Load, null, null)),
            new GodotSessionOperationChecks(this, token => CreateCaptureDiagnosticHarness().CheckMovementCancellationAsync(token), () => _hostOperations.Stopping, message => GD.Print(message)),
            equipment, ui, profile, new DiagnosticProfileEvidence(() => SharedProfilePath, SaveSharedProfile),
            CreateCaptureDiagnosticHarness().Acceptance, samples,
            new GodotCommonContactFixture(() => _sceneResult?.Root, () => _player),
            new DiagnosticRetiredActorProbe(() => [.. Rac1World.Presentation.HostileNodes.Values], PlayerLife.ApplySourceDamage), health,
            () => PlayerEquipmentConfigurationIdentity, audio, _playerAudio!.Events);
        return new(_args, [.. TrilogySourceDefinitions.All.Select(source => new DiagnosticSourceRequirement(source.Game, source.Authority.BuildId))], ports,
            new DiagnosticScenarioRunner(new GodotDiagnosticSources(() => [.. Sources.Setup.Library.Attached.Values.Select(source => new DiagnosticSourceSnapshot(source.Game, source.Identity.BuildId, true))]),
                new GodotDiagnosticFrameClock(this)), _hostOperations, (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code),
            message => GD.Print(message), message => GD.PrintErr(message));
    }

    private Task RunRac1VeldinRetailPauseSmokeAsync() => CreateUiDiagnosticHarness().RunVeldinPauseAsync(LegacyDiagnosticCancellation.Current);
    private Rac1WorldRouteDiagnosticHarness CreateRac1WorldRouteDiagnosticHarness()
    {
        var movement = CreateMovementDiagnosticHarness(); var worlds = new GodotDiagnosticWorldObservations(WorldScenes);
        var ui = CreateUiDiagnosticHarness();
        var lifetime = new GodotRac1WorldRouteLifetimeFixture(this,
            () => new(worlds.ReadWorld(), _rac1NovalisMechanisms.Count, _rac1TaxiNodes.Count, _rac1TaxiCollisionBodies.Count,
                _rac1Music?.Tracks.Count ?? 0, _rac1MusicDecoded.Count, _rac1MusicReader is not null, _rac1TaxiCameraWorldOwned,
                _rac1FlyingTaxi is not null || _rac1TaxiPendingStop is not null || _rac1TaxiRiderFrame is not null, SharedProfilePath is not null),
            () => _worldHost.Root, () => _rac1TaxiCollisionBodies.Values, () => _player?.Camera);
        return new(movement.Ports,
            () => new GodotRac1NovalisRouteFixture(() => _rac1NovalisMechanisms, () => _player, worlds),
            () => new GodotRac1NovalisMusicFixture(worlds, () => new(worlds.ReadWorld(), _rac1Music is { } music
                    ? [.. music.Tracks.Select(track => new Rac1NovalisMusicTrackDiagnostic(track.Index, track.Sector, track.DurationSeconds))] : [],
                _rac1MusicArea, _rac1MusicSlot, _rac1MusicIntroRemaining, _rac1MusicDecoded.Count,
                [.. _worldHost.AudioDiagnostics.Select(item => item.Message)], _worldHost.AudioStatusLine), OnRac1NovalisCrankMusic, TickRac1NovalisMusic),
            CreateRac1TaxiDiagnosticFixture, ui, lifetime, ui.Menus,
            new DiagnosticNavigationActions(destination => TrilogyWorldProviders.Resolve(destination) is { } native && OnDestinationChosen(native)),
            new DiagnosticSourceAttachmentFixture(Sources.Setup.Library, RefreshPlayerSourceAvailability));
    }

    private IRac1TaxiDiagnosticFixture CreateRac1TaxiDiagnosticFixture() => new GodotRac1TaxiDiagnosticFixture(new()
    {
        Session = () => _rac1MetropolisTaxis,
        Nodes = () => _rac1TaxiNodes,
        Hulls = () => _rac1TaxiCollisionBodies,
        Hovers = () => _rac1TaxiHovers.Count,
        Player = () => _player,
        Pending = () => _rac1TaxiPendingStop,
        HeroTarget = () => _rac1TaxiHeroTarget,
        Flying = () => _rac1FlyingTaxi,
        Rider = () => _rac1TaxiRiderFrame,
        CameraOwned = () => _rac1TaxiCameraWorldOwned,
        Hud = GetRac1MetropolisTaxiHudLine,
        Input = TryHandleRac1MetropolisTaxiInput,
        Abort = AbortRac1MetropolisTaxiRide,
        Landing = TryResolveRac1TaxiLanding,
        ToNative = ToTaxiPoint,
        ToScene = FromTaxiPoint,
        Basis = ToTaxiCabBasis,
    }, new GodotDiagnosticWorldObservations(WorldScenes));

    private ShotListCliService CreateShotListCliService() => new(_args, (game, path) => RememberCommandLineSource(game, path),
        new GodotShotListCaptureFixture(this, _camera, camera => _activeCamera = camera, OpenDestinationFromBootstrap,
            () => _mode == Mode.World ? _world : null, FrameShowcaseCamera, ApplyOverlaySpec, UpdateWorldHud, CapturePresentation.Metadata),
        (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code), message => GD.Print(message), message => GD.PrintErr(message));

    private StressSwitchCliService CreateStressSwitchCliService() => new(
        new GodotStressWorldFixture(() => new(new GodotDiagnosticWorldObservations(WorldScenes).ReadWorld(),
                _worldHost.Root?.GetInstanceId(), _player?.GetInstanceId(), _rac1Campaign.Session.Campaign.CurrentLevel),
            path => _activeDestination is { } destination && _worldHost.Root is not null && !EnterProviderWorld(path, destination),
            ShowDestinationSelector, OpenDestinationFromBootstrap, _worldRoot, () => _world?.LevelId ?? -1, () => _player?.IsOnFloor()),
        new GodotDiagnosticFrameClock(this), (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code),
        message => GD.Print(message), message => GD.PrintErr(message));

    private void StartFusionLabTraversalSmoke(PlayerHost player)
    {
        var runner = new DiagnosticScenarioRunner(new GodotDiagnosticSources(() => []), new GodotDiagnosticFrameClock(this));
        var scenario = new FusionLabTraversalScenario(new GodotFusionLabTraversalFixture(player, () => _frame,
            new GodotDiagnosticWorldObservations(WorldScenes), ShowDestinationSelector, EnterFusionLab, () => _player is not null),
            message => GD.Print(message), message => GD.PrintErr(message));
        _hostOperations.Start("fusion-lab-traversal", async token =>
        {
            var result = await runner.RunAsync(scenario, explicitlyRequested: true, token);
            if (result.Status is not (DiagnosticRunStatus.Passed or DiagnosticRunStatus.Canceled))
                throw result.Error ?? new InvalidOperationException(result.Detail);
        }, lifetime: CancellationToken.None);
    }

    private void BuildScene(string scene)
    {
        _mode = Mode.Smoke;
        _sceneKind = scene;
        EnsurePlainEnvironment();

        var sun = new DirectionalLight3D { Name = "Sun", RotationDegrees = new Vector3(-55, -35, 0) };
        AddChild(sun);

        _worldRoot.AddChild(new MeshInstance3D
        {
            Name = "Floor",
            Mesh = new PlaneMesh { Size = new Vector2(12, 12) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.2f, 0.24f) },
        });
        _worldRoot.AddChild(new MeshInstance3D
        {
            Name = "SmokeCube",
            Mesh = new BoxMesh { Size = new Vector3(2, 2, 2) },
            Position = new Vector3(0, 1, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.45f, 0.15f) },
        });

        _camera.Position = new Vector3(4.5f, 3.5f, 5.5f);
        _camera.LookAt(new Vector3(0, 0.8f, 0), Vector3.Up);
        _ui.AddChild(new Label { Name = "Banner", Text = $"One Big Package — {scene}", Position = new Vector2(16, 12) });

        if (scene.Equals("hud", StringComparison.OrdinalIgnoreCase))
        {
            BuildHudFixture();
        }
    }

    private void BuildHudFixture()
    {
        EnsurePlayerHud();
        var snapshot = _hudState.BeginSession(new HudPresentationState(
            Health: new HudHealth(3, 6, "nanotech", HudLifeState.Alive),
            Bolts: new HudCurrency("bolts", 12_450),
            CurrentWeapon: new HudWeapon(
                PresentationKey: "hud.fixture.vector-blaster",
                NameKey: "Vector Blaster",
                Ammo: new HudAmmo(7, 12)),
            ContextPrompt: new HudContextPrompt(
                PromptId: "hud-fixture-console",
                ActionId: "interact",
                MessageKey: "Activate test console",
                Progress: 0.42)));
        _playerHud!.Render(snapshot);
    }


    private bool Rac1PlanetMapOpen => _navigation.PlanetMapOpen;
    private Task WaitForWorldPreparationFrameAsync(CancellationToken token) =>
        GodotAsyncWait.SignalAsync(GetTree(), SceneTree.SignalName.ProcessFrame, token);

    private ObpDestination? _activeDestination => _worldScenes?.NativeDestination;
    private string? _currentDestinationId => _worldScenes?.DestinationId;

    // Established bootstrap, diagnostic and world-entry method contracts.
    public bool TryReturnSelectorToMenu() => _navigation.TryReturnSelectorToMenu();
    public bool TryReturnWorldToDestinations() => _navigation.TryReturnWorldToDestinations();
    public bool TryOpenRac1PlanetMap() => _navigation.TryOpenPlanetMap();
    public bool TryCloseRac1PlanetMap() => _navigation.TryClosePlanetMap();
    public Rac1PlanetTravelStartResult TravelRac1CampaignTo(int id) => _navigation.TravelRac1CampaignTo(id);
    public void ShowTitleFromBootstrap() => _navigation.ShowTitle();
    public void ShowDestinationSelectorFromBootstrap()
    {
        Sources.Initialize();
        _navigation.ShowSelector();
    }
    public void OpenDestinationFromBootstrap(string id) => _navigation.OpenFromBootstrap(id);

    public void OpenLegacyRac2DestinationFromBootstrap()
    {
        int levelId = _args.Planet is { } token
            ? OBP.RAC2.Rac2PlanetCatalogue.Resolve(token) ?? _args.Rac2Level
            : _args.Rac2Level;
        OpenDestinationFromBootstrap($"rac2:LEVEL{levelId}");
    }
    public void RunRac2StressSwitchFromBootstrap(string sequence) =>
        _hostOperations.Start("stress-switch", token => CreateStressSwitchCliService().RunStressSwitchAsync(sequence, token),
            lifetime: CancellationToken.None);
    public void OpenRac1CampaignCurrentFromBootstrap()
    {
        Sources.Initialize();
        OpenDestinationFromBootstrap($"rac1:LEVEL{_rac1Campaign.Session.Campaign.CurrentLevel}");
    }

    private void ShowTitleScreen() => _navigation.ShowTitle();
    private void ClearTitleScreen() => _navigation.ClearTitle();
    private void ShowDestinationSelector() => _navigation.ShowSelector();
    private bool OnDestinationChosen(ObpDestination destination) => _navigation.Choose(destination);
    private bool EnterProviderWorld(string path, ObpDestination destination) =>
        _navigation.EnterProviderWorld(path, destination);
    private bool EnterFusionLab() => _navigation.EnterLab();
    private void DecorateFusionLab(FusionLabDefinition definition, bool nativeAvatarAvailable) =>
        _navigation.DecorateLab(definition, nativeAvatarAvailable);
    private void ClearFusionLabStatus() => _navigation.ClearLab();
    private void DestinationError(string message) => _navigation.Error(message);

    private void AbandonFailedRac1Travel()
    {
        if (_rac1Campaign.Session.Travel.TransitionActive &&
            !_rac1Campaign.Session.Travel.CurrentLevelCommitted)
            _rac1Campaign.Session.AbandonUncommittedHostLoad();
    }


    private UiModalCoordinator? _modals;
    private UiModalCoordinator Modals => _modals ??= CreateModalCoordinator();
    private PauseMenuUi? _pauseMenu => Modals.PauseMenu;
    private WeaponQuickSelectUi? _quickSelect => Modals.QuickSelect;
    private bool _debugOutputEnabled => Modals.DebugOutputEnabled;
    private bool PauseMenuOpen => Modals.PauseOpen;
    private bool ControllerSettingsOpen => Modals.SettingsOpen;
    private bool QuickSelectOpen => Modals.QuickSelectOpen;
    private SourceWeaponKey? _previousSelectedWeapon { get => Modals.PreviousSelectedWeapon; set => Modals.PreviousSelectedWeapon = value; }
    private ulong _lastQuickSelectTap { set { if (value == 0) Modals.ResetWheelGesture(); } }
    private SourceWeaponKey CurrentWeaponKey => FusionWeapons.Session is { Selected.SourceGame: ObpSourceGame.Rac2 or ObpSourceGame.Rac3 }
        ? FusionWeapons.Session.Selected : new(ObpSourceGame.Rac1, _rac1Weapons.CurrentItemId);

    private UiModalCoordinator CreateModalCoordinator()
    {
        var equipment = new ModalEquipmentPorts(
            Catalog: ReadPlayerEquipment,
            QuickSlots: ReadPlayerQuickSlots,
            Equip: SelectPlayerEquipment,
            Unequip: UnequipPlayerEquipment,
            SetGadget: SetPlayerGadgetEnabled,
            AssignSlot: AssignPlayerQuickSelect,
            AssignNativeSlot: (slot, id) => _playerNative is not null
                ? AssignPlayerQuickSelect(slot, $"rac1/item/{id}").Accepted
                : _rac1Weapons.TryAssignQuickSelect(slot, id),
            SelectNativeWeapon: OnRac1WeaponSelectionRequested,
            CurrentWeapon: () => CurrentWeaponKey,
            SelectWeapon: FusionWeapons.Select,
            CanRepeatWeapon: key => key.SourceGame == ObpSourceGame.Rac1
                ? _rac1Weapons.Owns((Rac1WeaponId)key.NativeItemId)
                : FusionWeapons.Session is not null && FusionWeaponSession.Weapons.Contains(key),
            WheelEntries: ReadWeaponQuickSelectEntries);

        return new UiModalCoordinator(new UiModalPorts(
            Host: this,
            CanPause: () => _mode == Mode.World && _args.CaptureFrame is null,
            CanQuickSelect: () => _mode == Mode.World && !Rac1PlanetMapOpen,
            BuildPauseMenu: CreatePauseMenuPresentation,
            Player: () => _player,
            HasSourceView: () => Sources.HasView,
            SetSourceVisible: visible => Sources.SetVisible(visible),
            SetSelectorVisible: visible => _navigation.SetSelectorVisible(visible),
            SetPlayerAudioPaused: SetPlayerAudioPaused,
            TryMissionInteraction: TryHandleRac1MissionInteraction,
            TryTaxiInteraction: TryHandleRac1MetropolisTaxiInput,
            Equipment: equipment));
    }

    public void ShowControllerSettingsFromMenu()
    {
        if (!ControllerSettingsOpen) Modals.OpenSettings(fromPause: false);
    }

    public bool TryOpenPauseMenu() => Modals.OpenPause();
    public bool TryClosePauseMenu() => Modals.TryClosePause();
    public bool TryCloseControllerSettings() => Modals.TryCloseSettings();
    private void ClosePauseMenu() => Modals.ClosePause();
    private void CloseWeaponQuickSelect(SourceWeaponKey? weapon = null) => Modals.CloseQuickSelect(weapon);
    private void TearDownInteractiveOverlays() => Modals.RetireAll();
    public bool TryOpenWeaponQuickSelect(InputEvent input) => Modals.OpenQuickSelect(input);

    private void ReturnToWorldsFromPause()
    {
        ClosePauseMenu();
        ShowDestinationSelector();
    }

    private PauseMenuUi CreatePauseMenuPresentation()
    {
        var equip = Modals.Equipment;
        var menu = new PauseMenuUi
        {
            ProcessMode = ProcessModeEnum.Always,
            RetailEnabled = !OrdinaryPlayerSession && _player?.HasRetailPresentation == true && Rac1RetailUiAssets.IsLoaded,
            CatalogReader = OrdinaryPlayerSession ? equip.Catalog : null,
            SlotReader = OrdinaryPlayerSession ? equip.QuickSlots : null,
            CatalogEquip = equip.Equip,
            CatalogUnequip = equip.Unequip,
            CatalogGadget = equip.SetGadget,
            CatalogAssign = equip.AssignSlot,
            CatalogPreview = _avatarPresentation.CreateAppearancePreview,
            ActorStatusReader = ActorHost.ReadStatus,
            SharedProfileStatusReader = () => _profileStatus,
            SaveNoticeReader = () => PlayerSaveNotice,
            ActorReset = ResetPlacedActor,
            EffectStatusReader = () =>
            {
                var resources = _worldScenes?.Current?.Resources;
                resources?.Effects?.Prepare();
                return resources?.Effects?.Status ?? resources?.EffectsFailure ?? "World effects are unavailable.";
            },
            EffectControl = operation => _worldScenes?.Current?.Resources.Effects?.Control(operation) ??
                new(false, _worldScenes?.Current?.Resources.EffectsFailure ?? "World effects are unavailable."),
            CatalogIcon = entry => entry.Definition.Item is { SourceGame: ObpSourceGame.Rac1 } item &&
                Rac1WeaponIconCatalog.Ordinary((Rac1WeaponId)item.NativeItemId) is { } icon
                ? Rac1RetailUiAssets.Sprite(icon.AnimationId, icon.Frame) : null,
            LabAvailable = OrdinaryPlayerSession,
            Weapons = Rac1WeaponProfiles.Additional.Select(profile => profile.Item)
                .Prepend(Rac1WeaponId.FirstRanged)
                .OrderBy(weapon => (int)weapon)
                .Select(weapon => new PauseMenuUi.RetailItem(
                    (int)weapon, Rac1WeaponProfiles.Name(weapon),
                    _rac1Weapons.Owns(weapon), _rac1Weapons.Equipped == weapon,
                    Rac1WeaponIconCatalog.Ordinary(weapon)?.AnimationId))
                .ToArray(),
            Gadgets = (Rac1RetailUiAssets.NativeMenuPages?.NativeGadgetsGrid.Groups
                .SelectMany(group => group.Cells).Select(cell => cell.NativeId) ??
                Enum.GetValues<Rac1TraversalEquipmentId>().Select(gadget => (int)gadget))
                .Select(id =>
                {
                    var gadget = (Rac1TraversalEquipmentId)id;
                    string name = Enum.IsDefined(gadget) ? gadget.ToString() switch
                    {
                        "HeliPack" => "Heli-Pack",
                        "ThrusterPack" => "Thruster-Pack",
                        "HydroPack" => "Hydro-Pack",
                        "OxygenMask" => "O2 Mask",
                        "Magneboots" => "Magneboots",
                        "Grindboots" => "Grindboots",
                        _ => gadget.ToString(),
                    } : $"Native item {id}";
                    return new PauseMenuUi.RetailItem(id, name,
                        _rac1Weapons.OwnsNativeItem(id), false);
                }).ToArray(),
            QuickSlots = (_playerNative?.Assignments.Capture().QuickSelect ?? _rac1Weapons.QuickSelect
                .Select(id => id == 0 ? (OBP.Runtime.Gameplay.SourceWeaponKey?)null : new OBP.Runtime.Gameplay.SourceWeaponKey(ObpSourceGame.Rac1, id)).ToArray()).Select((key, slot) =>
            {
                // The retained native menu only admits native icon/item IDs.
                // Foreign slots remain real in the OBP read model and wheel;
                // #259 owns their full pause-menu presentation.
                int id = key is { SourceGame: ObpSourceGame.Rac1 } native ? native.NativeItemId : 0;
                Rac1WeaponId weapon = (Rac1WeaponId)id;
                var icon = Rac1WeaponIconCatalog.Ordinary(weapon);
                return new PauseMenuUi.RetailItem(id,
                    key is { SourceGame: not ObpSourceGame.Rac1 } foreign ? $"{FusionWeaponHost.WeaponName(foreign)} [{SourceIdentityWire.FormatSource(foreign.SourceGame)}]" :
                    id == 0 ? $"Slot {slot + 1}: Empty" :
                    Enum.IsDefined(weapon) ? Rac1WeaponProfiles.Name(weapon) : $"Native item {id}",
                    id != 0 && _rac1Weapons.OwnsNativeItem(id),
                    id != 0 && _rac1Weapons.Equipped == weapon,
                    icon?.AnimationId);
            }).ToArray(),
            // Retail Items kind=0 reads item_available[]. Kind=1 reads
            // independent alternate_item_available[] progression flags,
            // which this campaign host does not yet model or invent.
            NativePrimaryItemAvailability = _rac1Weapons.Items,
            Items = Enumerable.Range(0, Rac1NativeInventoryLayout.ItemCount)
                .Where(id => _rac1Weapons.OwnsNativeItem(id) &&
                    !Enum.IsDefined((Rac1WeaponId)id) &&
                    !Enum.IsDefined((Rac1TraversalEquipmentId)id) &&
                    !(Rac1RetailUiAssets.NativeMenuPages?.NativeGadgetsGrid.Groups
                        .Any(group => group.Cells.Any(cell => cell.NativeId == id)) ?? false))
                .Select(id => new PauseMenuUi.RetailItem(id, $"Native item {id}", true, false))
                .ToArray(),
            DebugOutputEnabled = _debugOutputEnabled,
            MovementStatesAvailable = _player?.CanSaveMovementState == true,
            WeaponPreviewEnabled = _persistentPlayer.WeaponPreview is not null,
            FusionTestAvailable = FusionWeapons.CanRunFixture,
            FusionTestEnabled = FusionWeapons.TestActive,
            EquipmentStatus = _player is { } activePlayer
                ? FusionWeapons.TestActive ? "Three-game Veldin test · temporary inventory\nOriginal damage · OBP hand mounts / spread feedback\nHold Triangle / Q to select; Circle / E to fire."
                : $"Weapons: {(_persistentPlayer.WeaponPreview is null ? "campaign inventory" : "full arsenal preview")}\nPack: {activePlayer.Rac1EquipmentLabel}\nSwingshot preview: {(activePlayer.Rac1DevelopmentSwingshotPreview ? "on" : "off")}"
                : string.Empty,
        };
        menu.EquipRequested += equip.SelectNativeWeapon;
        menu.FusionTestRequested += FusionWeapons.ToggleTest;
        menu.LabRequested += () => { ClosePauseMenu(); EnterFusionLab(); };
        menu.QuickSelectAssignRequested = equip.AssignNativeSlot;
        menu.ResumeRequested += () => ClosePauseMenu();
        menu.ControllerSettingsRequested += () => Modals.OpenSettings(fromPause: true);
        menu.DebugOutputChanged += SetDebugOutputEnabled;
        menu.SaveMovementStateRequested += SaveMovementState;
        menu.LoadMovementStateRequested += () => StartMovementStateLoad(MovementStatePath);
        menu.WeaponPreviewRequested += () => { ClosePauseMenu(); SetRac1WeaponPreview(_persistentPlayer.WeaponPreview is not null); };
        menu.RefillPreviewRequested += () => { ClosePauseMenu(); if (_persistentPlayer.WeaponPreview is not null) SetRac1WeaponPreview(false); };
        menu.PackPreviewRequested += () => { _player?.CycleRac1DevelopmentPack(); ClosePauseMenu(); };
        menu.SwingshotPreviewRequested += () => { _player?.ToggleRac1DevelopmentSwingshot(); ClosePauseMenu(); };
        menu.WorldsRequested += ReturnToWorldsFromPause;
        menu.QuitRequested += () =>
            ApplicationLifecycle.RequestQuit(this, "pause-menu-quit");
        return menu;
    }
    private void SetDebugOutputEnabled(bool enabled)
    {
        Modals.DebugOutputEnabled = enabled;
        _player?.SetDebugOutputEnabled(enabled);
        if (_playerHud is not null && IsInstanceValid(_playerHud))
            _playerHud.DebugOutputEnabled = enabled;
        if (!enabled)
        {
            if (_inspector is { IsOpen: true }) _inspector.Toggle();
            _overlay?.Clear();
            if (_movementStateNotice is not null && IsInstanceValid(_movementStateNotice))
                _movementStateNotice.Visible = false;
            ++_movementStateNoticeGeneration;
        }
        UpdateWorldHud();
        UpdatePlayerHud();
    }

    private WeaponQuickSelectUi.Entry[] ReadWeaponQuickSelectEntries()
    {
        var available = Rac1WeaponProfiles.Additional.Select(p => p.Item).Prepend(Rac1WeaponId.FirstRanged)
            .Where(_rac1Weapons.Owns).ToArray();
        // Respect native ring order, then expose owned implemented items not yet
        // inserted by the incomplete acquisition adapter. Extra pages are OBP UI.
        var order = _rac1Weapons.QuickSelect.Where(id => available.Contains((Rac1WeaponId)id))
            .Select(id => (Rac1WeaponId)id).Concat(available).Distinct().ToArray();
        var entries = order.Select(weapon => new WeaponQuickSelectUi.Entry(new(ObpSourceGame.Rac1, (int)weapon),
            weapon == Rac1WeaponId.FirstRanged ? "Bomb Glove" : Rac1WeaponProfiles.Get(weapon).Name,
            (int)weapon == 9 ? $"{_rac1AdditionalWeapons?.Cargo.Count ?? 0} captured" :
            Rac1ItemAmmoDescriptors.Get((int)weapon).MaxAmmo > 0 ? $"{_rac1Weapons.GetAmmo((int)weapon)} ammo" : "")).ToArray();
        if (FusionWeapons.Session is not null && _playerNative is null)
        {
            var keys = _persistentPlayer.Equipment.WeaponOrder();
            entries = keys.Select(key => key.SourceGame == ObpSourceGame.Rac1 && !FusionWeapons.TestActive ? entries.Single(e => e.Weapon == key) :
                new WeaponQuickSelectUi.Entry(key, FusionWeaponHost.WeaponName(key) + $" [{SourceIdentityWire.FormatSource(key.SourceGame)}]",
                    FusionWeapons.Sources.Assets?.Available(key) == false ? "Source unavailable" : FusionWeapons.Detail(key))).ToArray();
        }
        if (_playerNative is not null)
        {
            var catalog = ReadPlayerEquipment();
            entries = _persistentPlayer.Equipment.QuickSelectOrder().Select(key =>
            {
                if (key is null) return new WeaponQuickSelectUi.Entry(null, "Empty", "Unassigned slot");
                var entry = catalog.Single(entry => entry.Definition.Item == key);
                return new WeaponQuickSelectUi.Entry(key, entry.Definition.DisplayName + $" [{SourceIdentityWire.FormatSource(entry.Definition.SourceGame)}]",
                    entry.Reason ?? (entry.State.Capacity is { } capacity ? $"{entry.State.Ammo}/{capacity}" : ""));
            }).ToArray();
        }
        return entries;
    }
}
