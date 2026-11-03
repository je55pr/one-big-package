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
/// Source-game parsing ends at the provider boundary; this root composes the
/// presentation, input, navigation, diagnostics and live-world owners.
///
/// Small <c>--test-scene</c> fixtures remain available for deterministic host
/// checks. Historical GC command-line aliases are translated to canonical
/// destinations rather than selecting a separate runtime path.
/// </summary>
public partial class OBPGame : Node3D
{
    private enum Mode { Smoke, Picker, Selector, World, Title }


    public void LoadMovementStateFromBootstrap() => Capture.Reproduction.LoadMovementStateFromBootstrap();

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
                Capture.VerificationIfStarted?.Cancel(game);
                FusionWeapons.ResetSources();
                if (game == ObpSourceGame.Rac1)
                {
                    _playerAudio?.RetireWorld();
                    _equipmentPresentation.WeaponAudio.Reset();
                }
            }, FusionWeapons.PrepareSources),
            message => GD.Print(message), message => GD.PrintErr(message));
        return new(this, setup, new(EnsureRac1CampaignPersistenceInitialized, () =>
        {
            _navigation.CancelNavigation(); _navigation.RetireMenuPages(); _mode = Mode.Picker; _sceneKind = "sources";
            _modals?.RetireAll();
            TeardownWorld(); GodotSmokeSceneFixture.EnsurePlainEnvironment(this);
            _navigation.RetireSelector();
            if (_worldHud is not null && IsInstanceValid(_worldHud)) _worldHud.Visible = false;
            _camera.Current = true; _activeCamera = _camera;
        }, ShowTitleScreen, ShowDestinationSelector, ShowControllerSettingsFromMenu, AbandonFailedRac1Travel,
            (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code)),
            () => new(_profileStatus, _profileWriteBlocked, _rac1CampaignRestoreWarning), () => _debugOutputEnabled,
            _hostOperations.Token, DisplayServer.GetName() == "headless" || AutomationRunPolicy.IsAutomated(OS.GetCmdlineUserArgs(), OS.GetCmdlineArgs()));
    }


    private GodotApplicationPresentation? _presentation;
    private GodotApplicationPresentation Presentation => _presentation ??= new(this, () => _debugOutputEnabled);
    private Node3D _worldRoot => Presentation.WorldRoot;
    private Node3D _playerRoot => Presentation.PlayerRoot;
    private CanvasLayer _ui => Presentation.Ui;
    private Camera3D _camera => Presentation.Camera;

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
        () => _worldScenes?.Current?.Resources.Rac1Gameplay?.Lifecycle.RetryPanel is not null,
        () => { FusionWeapons.ClearOnDeath(); Rac1WrenchHost.ResetOrFallback(_worldScenes?.Current?.Resources.Rac1Gameplay?.Wrench, _playerAudio, _player); },
        (before, after) => _playerAudio?.NotifyDamage(before, after), () => RefreshRac1HudState(), _hudState,
        () => Rac1HudProjection.Capture(_rac1Nanotech, _rac1Weapons, capturedMobyCount: _persistentPlayer.Rac1Use.Session?.Cargo.Count ?? 0,
            swimming: _player?.Rac1SwimState, ownsOxygenMask: _player?.Rac1MovementEquipment.OxygenMask == true),
        FusionWeapons.Hud));
    private FusionWeaponHost? _fusionWeapons;
    private FusionWeaponHost FusionWeapons => _fusionWeapons ??= CreateFusionWeaponHost();
    // Persistent player owns selection/input; the engine root only supplies deferred sources.
    private IRac1WeaponPlayerInput Rac1CombatInput => _persistentPlayer.CombatInput ??=
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
                () => QuickSelectOpen, message => Capture.Reproduction.Report(message)),
            new(previous => _equipmentPresentation.RefreshRac1HeldItem(previous, Rac1HandPresentation, _avatarPresentation),
                () => _equipmentPresentation.HoldSelectedRac1HandItem(Rac1HandPresentation, _avatarPresentation),
                () => RefreshRac1HudState(),
                () => _equipmentPresentation.WeaponAudio.Reset(),
                weapon => _equipmentPresentation.NativeViews.PlaySelection(weapon)));

    private FusionWeaponHost CreateFusionWeaponHost() => new(this, new(
        Options: () => new(_playerNative?.Weapons, Capture.ReplayIfStarted?.Recorder?.Reader is { Header.Weapons: null },
            _args.FusionTest, _args.FixedSeed ?? 1440, OrdinaryPlayerSession, _activeDestination?.DestinationId,
            Capture.ReplayIfStarted?.Recorder is not null, Sources.Setup.Access == SourceAccessMode.OrdinaryPlay || _args.CaptureFrame is not null || _args.ShotsPath is not null),
        Snapshot: () => new(_player is not null, Capture.Reproduction.IsLoading, PlayerLife.Alive, _player?.Scripted == true,
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
            _args = _args with { FusionTest = enabled };
            ClosePauseMenu();
            OnDestinationChosen(_activeDestination!);
        },
        Report: Capture.Reproduction.Report, Record: (kind, fact) => Capture.Replay.RecordGameplayFact(kind, fact)),
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
                (damage, accepted) => Capture.Replay.RecordGameplayFact("damage", new { Intent = damage, Accepted = accepted })),
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

    private static System.Numerics.Vector3 ToNumerics(Vector3 value) => new(value.X, value.Y, value.Z);
    private Rac1WorldModule Rac1World => _worldScenes?.Current?.Resources.Rac1Gameplay
        ?? throw new InvalidOperationException("R&C1 gameplay requires an active scene lease.");
    private Rac1BoltWallet _rac1BoltWallet { get => _persistentPlayer.Rac1Wallet(_rac1Campaign.Wallet); set => _rac1Campaign.Wallet = value; }
    private Rac1RatchetNanotechSession _rac1Nanotech => _persistentPlayer.Rac1Health;
    private Rac1WeaponInventory _rac1Weapons => _persistentPlayer.Rac1Weapons(FusionWeapons.Session, FusionWeapons.TestActive, _rac1Campaign.Session.Weapons);
    private Rac1HeldItemSession _rac1HeldItem => _persistentPlayer.Rac1Held;


    private void RefreshRac1HudState(params HudFeedbackDraft[] feedback)
    {
        if (_worldScenes?.Current?.Resources.Rac1Gameplay is { } gameplay) gameplay.Hud.Refresh(feedback);
        else if (OrdinaryPlayerSession && _playerSession is not null) PlayerLife.PublishHud(feedback);
    }

    private readonly PlayerAvatarPresentationHost _avatarPresentation = new();
    private readonly PlayerEquipmentPresentationBinder _equipmentPresentation = new();
    // Borrowed source/player/world facts only. Equipment presentation owns the visibility rules.
    private Rac1HandPresentationContext Rac1HandPresentation => new(
        _rac1HeldItem, _rac1Weapons, FusionWeapons.Session,
        _player?.Rac1WaterHandItemHidden == true, _player?.Rac1WrenchFrame.Active == true,
        _worldScenes?.Current?.Resources.Rac1Gameplay?.Wrench.ThrowActive == true);
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
                _rac1Campaign.Session.Weapons, _rac1Campaign.Wallet, _rac1Campaign.CollectedNovalisGoldBolts,
                _rac1Campaign.Session.BuriedBolts))).RetainUnknownFields(previous),
        RestoreCampaign: _rac1Campaign.Restore,
        RefreshPlayerSources: RefreshPlayerSourceAvailability,
        WritesSuspended: () => Sources.Setup.ProfileSuspended,
        PlaytestActive: () => Capture.ReplayIfStarted?.Recorder is not null,
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

    private void EnsurePlayerSession()
    {
        if (_playerSession is not null) return;
        Sources.Initialize();
        if (!_persistentPlayer.Ensure(PlayerEntryMode, _rac1Campaign.Session.Weapons,
            _rac1Campaign.Wallet.Balance, _args.FixedSeed ?? 1440, new PlayerCameraPreferences())) return;
        if (_playerNative is not null) FusionWeapons.BindPersistent(_playerNative.Weapons);
        RefreshPlayerSourceAvailability();
        InitializeSharedProfile();
        if (Capture.ReplayIfStarted?.TakeStartingProfile() is { } capture) ApplySharedProfile(capture);
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
    internal bool BugReportModalOpen => _bugReporting?.IsOpen == true || Capture.ReplayIfStarted?.Recorder?.BugInputSuspended == true;
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
    private DebugOverlay? _overlay => Presentation.Overlay;
    private WorldInspectorPanel? _inspector => Presentation.Inspector;
    private PlayerHost? _player => Presentation.Player;
    private int _worldSwitches => _worldScenes?.ActivationCount ?? 0;

    private Camera3D _activeCamera { get => Presentation.ActiveCamera; set => Presentation.ActiveCamera = value; }
    private Label? _worldHud => Presentation.WorldHud;
    private PlayerHud? _playerHud => Presentation.PlayerHud;
    private string _loadSummary => _navigation?.LoadSummary ?? string.Empty;

    public override void _Ready()
    {
        ApplicationLifecycle.InstallWindowCloseInterception(GetTree());
        ApplicationLifecycle.SetQuitPreparation(PrepareForQuitAsync);
        _rac1Campaign.Persistence.Bind(new(
            () => _args.Rac1CampaignPersistenceRequested, () => _args.Rac1CampaignSavePath,
            () => ProjectSettings.GlobalizePath("user://rac1-campaign.json"),
            () => _persistentPlayer.WeaponPreview is not null, () => FusionWeapons.TestActive,
            () => _rac1BoltWallet, message => GD.PushWarning(message), message => GD.Print(message)));
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
            Presentation: key => _equipmentPresentation.Observe(key, _equipmentPresentation.ModelAvailable(key, _avatarPresentation, _equipmentPresentation.NativeViews.Assets, FusionWeapons.Sources.Assets), _equipmentPresentation.Assets(OrdinaryPlayerSession, _worldScenes?.Current?.Resources.Rac1Gameplay?.Loot.PickupAssets) is not null),
            Context: () => new(_player?.HasRac1SwingshotTargets == true,
                _worldScenes?.Current?.Resources.Rac1Gameplay?.Mechanisms.HasWater == true),

            SelectWeapon: FusionWeapons.Select,
            ResetWeaponPresentation: () =>
            {
                _persistentPlayer.Rac1Use.ClearUseTicks();
                _worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.Arsenal.SetHeld(false);
                _persistentPlayer.Rac1Use.StopPyro();
                _equipmentPresentation.WeaponAudio.Reset(); FusionWeapons.ResetHeldUse();
                _equipmentPresentation.RefreshRac1HandVisibility(Rac1HandPresentation, _avatarPresentation); RefreshRac1HudState();
            },
            ApplyMovementEquipment: (movement, backpack) => _player?.ApplyPlayerEquipment(movement, backpack),
            Record: command => Capture.Replay.RecordCommand(command)));
        _playerAudio ??= new(new GodotRac1PlayerAudioPlayback(this, () => _player, intent => _worldHost.PlayEffect(intent)),
            () => _avatarPresentation.Clock?.Tick ?? 0, () => _equipmentPresentation.WeaponAudio.Reset(),
            reason => GD.Print("[player-audio] " + reason));
        if (!Capture.ReplayBookmarkNavigation.Initialize(ref _args)) return;
        if (_args.FusionTest && _args.Destination is null) _args = _args with { Destination = "rac1:LEVEL0" };
        var bugStartup = BugReportStartup.Initialize(this, _args, _hostOperations.Token);
        _args = bugStartup.Args;
        if (!bugStartup.Ready) return;
        if (bugStartup.Capture is { } capture)
            _bugReporting = new(capture, _hostOperations, new(this, () => (Node)Capture.ReplayIfStarted!.Recorder!, () => Capture.ReplayIfStarted!.Recorder!.BugWriter,
                () => Capture.ReplayIfStarted!.Recorder!.ResumeAfterBugOverlay(), notice => Capture.ReplayIfStarted!.Recorder!.ShowBugNotice(notice),
                Capture.Replay.Build.Matches, GodotCaptureReplayStartup.PhysicsEngine, WaitForWorldPreparationFrameAsync,
                (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code)));
        var playtestStartup = GodotCaptureReplayStartup.Initialize(this, Capture.Replay, _args, Capture.CreatePlaytestDriverPorts());
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
            AbandonFailedTravel: AbandonFailedRac1Travel,
            ReportFailure: (error, superseded) => ApplicationLifecycle.ReportWorldPreparationFailure(this, error, superseded),
            EnterWorld: (id, load, destination, sourcePath, lab) => EnterWorld(id, load, destination, sourcePath, lab),
            EnterPage: isTitle =>
            {
                _modals?.RetireAll();
                _mode = isTitle ? Mode.Title : Mode.Selector;
                _sceneKind = isTitle ? "title" : "destinations";
                TeardownWorld();
                GodotSmokeSceneFixture.EnsurePlainEnvironment(this);
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
            bootstrap.StartupVisibilityCheck = () => Diagnostics.CreateUiDiagnosticHarness().Start("rac1-startup-visibility-smoke");
        if (_args.OrdinaryUiSmoke) Diagnostics.CreateUiDiagnosticHarness().Start("ordinary-ui-smoke");

        // Dedicated deterministic fixture through the same playable Lab host
        // used by the ordinary World Select button.
        if (_args.TestScene == "fusion-lab")
        {
            if (EnterFusionLab() && _args.CaptureFrame is { } fusionCapture)
            {
                if (_player is { } player) Diagnostics.StartFusionLabTraversalSmoke(player);
                _hostOperations.Start("capture", token => Capture.CaptureView.RunCaptureAsync(fusionCapture, token), lifetime: CancellationToken.None);
            }
            return;
        }

        // A --shots run works for any provider and drives the same neutral
        // destination/provider path used by ordinary world entry.
        if (_args.ShotsPath is { } shotList)
        {
            _hostOperations.Start("shots", token => Diagnostics.CreateShotListCliService().RunShotsAsync(shotList, token), lifetime: CancellationToken.None);
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
            GodotSmokeSceneFixture.EnsurePlainEnvironment(this);
        }

        GD.Print($"[OBPGame] ready — mode={_mode} scene='{_sceneKind}' " +
                 $"capture={_args.CaptureFrame?.ToString() ?? "off"} renderer={CaptureHarness.ActiveRenderer()} " +
                 $"providers={string.Join(",", TrilogyWorldProviders.All.Select(provider => provider.BuildId))}");

        if (_args.CaptureFrame is { } frame)
        {
            Engine.MaxFps = 60;
            _hostOperations.Start("capture", token => Capture.CaptureView.RunCaptureAsync(frame, token), lifetime: CancellationToken.None);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_mode != Mode.World || Capture.Reproduction.IsLoading || _player is null || _player.DevelopmentControls.FlyEnabled) return;
        _worldScenes?.Current?.Resources.GameplayTicks?.Run(GameplayTickPhase.Physics, delta);
    }

    public override void _Process(double delta)
    {
        GodotCapturePresentation.ReportHostOperationFailures(this, _hostOperations);
        if (_hostOperations.Stopping) return;
        _frame++;
        Sources.Tick(delta, _mode is Mode.World or Mode.Selector or Mode.Title);
        FusionWeapons.TickPreparation();
        ActorHost.PollPreparation();
        _worldScenes?.Current?.Resources.GameplayTicks?.Run(GameplayTickPhase.Prepare, delta);
        TickSharedProfile(delta);
        TickRac1CampaignSaves(delta);

        if (_mode == Mode.World)
        {
            if (Capture.ReplayIfStarted?.Recorder is { WaitingForEntry: true } or { PreparingWorld: true }) return;
            _worldScenes?.Current?.Resources.GameplayTicks?.RunWorldFrame(delta);
        }

        Capture.VerificationIfStarted?.Tick(Time.GetTicksMsec());
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_worldScenes?.Current?.Resources.GameplayTicks?.TryHandleInput(@event) == true)
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

        if (key.Keycode == Key.M && Rac1World.Weapons.Detector.Toggle())
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
        else if (_overlay is { } overlay && overlay.HandleKey(key.Keycode))
        {
            UpdateWorldHud();
        }
    }

    public bool TryHandleInteractiveBack() => _navigation.HandleBack(Modals);

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
        Capture.VerificationIfStarted?.Dispose();
        SaveSharedProfile("clean exit");
        // An external exit can bypass normal world teardown. Finish campaign
        // persistence before shutting down its owning Godot session.
        FlushRac1CampaignSaves("clean exit");
        ActorHost.Reset();
        _worldHost.Unload(releaseAudioImmediately: true);
        _playerAudio?.Dispose();
        _equipmentPresentation.WeaponAudio.Reset(freeImmediately: true);
        FusionWeapons.ResetWorld(freeImmediately: true);
        FusionWeapons.ResetSources();
        _worldScenes?.Dispose();
    }

    private Task PrepareForQuitAsync() => GodotApplicationShutdown.PrepareAsync(this,
        _modals, Capture.ReplayIfStarted, _bugReporting, Capture.VerificationIfStarted, _hostOperations, _worldScenes, ReleaseAudioForExit);

    public override void _ExitTree() => GodotApplicationShutdown.Exit(ReleaseAudioForExit,
        () => _playerLife, () => _fusionWeapons, _persistentPlayer, () => ApplicationLifecycle.ReportRootExit(Name));

    private void BuildSkeleton() => Presentation.BuildSkeleton();

    /// <summary>Free the current world sub-tree and everything it owns. Safe to call when nothing is loaded.</summary>
    private void TeardownWorld() => WorldScenes.Retire();

    private void RetireWorldBindings()
    {
        // Prevent in-flight source ticks from continuing through a retired world.
        _worldScenes?.Current?.Resources.RetireGameplayTicks();
        FlushRac1CampaignSaves("world teardown");
        ActorHost.Reset();
        PlayerLife.RetireWorld();
        ClearFusionLabStatus();
        CloseWeaponQuickSelect();
        TearDownInteractiveOverlays();
        TryCloseRac1PlanetMap();
        // A failure in one retirement step must not leave the old player node live and parented.
        try
        {
            ClearPlayerAvatarView();
            _worldScenes?.Current?.Resources.RetireGameplayBeforePlayer();
        }
        finally
        {
            try { Presentation.RetireWorldHud(_hudState, OrdinaryPlayerSession); }
            finally
            {
                try { Presentation.RetirePlayerAndInspection(); }
                finally { _worldScenes?.Current?.Resources.RetireGameplayAfterPlayer(); }
            }
        }
    }

    /// <summary>Delegate the overlay spec used by captures and individual shots.</summary>
    private void ApplyOverlaySpec(string? spec) => Presentation.ApplyOverlaySpec(spec);

    private void SpawnPlayer(RuntimeWorld world)
    {
        var player = Presentation.SpawnPlayer(world, _worldPlayerEntry, _worldEntrySnapReach, _args, _playerControlProfile,
            () => _worldScenes?.Current?.Resources.CrateDiagnostics.TryGetFocusPose(world, out var point, out var heading) == true
                ? new GodotSelectedPlayerSpawnPose(point, heading) : null);
        PlayerLife.BindPlayer(player);
        player.DevelopmentControls.SaveMovementStateRequested += Capture.Reproduction.SaveMovementState;
        player.DevelopmentControls.LoadMovementStateRequested += () => Capture.Reproduction.StartMovementStateLoad(Capture.Reproduction.MovementStatePath);
        AttachPlayerAvatarVisual(player);
        Rac1World.Arm(player);
        _worldScenes?.Current?.Resources.Rac3Gameplay?.Arm(player, OrdinaryPlayerSession);
    }

    // --- HUD ---------------------------------------------------------------

    private void EnsurePlayerHud() => Presentation.EnsurePlayerHud();

    private void UpdatePlayerHud() => Presentation.UpdatePlayerHud(_hudState.Current);


    private void UpdateWorldHud() => Presentation.UpdateWorldHud(_worldScenes?.Current, _worldHost, _worldSwitches, _mode == Mode.World);

    // Composition modules bind the owned components to narrow ports outside the root.
    private ApplicationComponents? _components;
    private ApplicationComponents Components => _components ??= new(
        Host: this, Args: () => _args, ReplaceArgs: args => _args = args, Operations: _hostOperations,
        Player: _persistentPlayer, Campaign: _rac1Campaign, Avatar: _avatarPresentation,
        Equipment: _equipmentPresentation, Hud: _hudState,
        Presentation: () => Presentation, WorldScenes: () => WorldScenes, WorldScenesIfCreated: () => _worldScenes, Sources: () => Sources,
        Fusion: () => FusionWeapons, Life: () => PlayerLife, Actors: () => ActorHost,
        Reproduction: () => Capture.Reproduction, Capture: () => Capture.Replay, CaptureIfStarted: () => Capture.ReplayIfStarted,
        CapturePresentation: () => Capture.CaptureView, Navigation: () => _navigation, Modals: () => Modals,
        Bugs: () => _bugReporting, Profiles: () => Profiles, PlayerAudio: () => _playerAudio,
        CombatInput: () => Rac1CombatInput, PlayerSession: () => _playerSession, PlayerNative: () => _playerNative,
        OrdinaryPlayer: () => OrdinaryPlayerSession, Rac1Weapons: () => _rac1Weapons, Rac1Held: () => _rac1HeldItem,
        Rac1Wallet: () => _rac1BoltWallet, Rac1Nanotech: () => _rac1Nanotech, CurrentWeapon: () => CurrentWeaponKey,
        IdleWorldHost: _idleWorldHost,
        Mode: () => (_mode == Mode.Title, _mode == Mode.Selector, _mode == Mode.World, _mode == Mode.Picker),
        Frame: () => _frame, OpenDestination: OpenDestinationFromBootstrap,
        EnterWorld: (id, load, destination, path) => EnterWorld(id, load, destination, path),
        RefreshPlayerSources: RefreshPlayerSourceAvailability,
        ApplyProgressionEvent: ApplyRac1CampaignProgressionEvent,
        CompleteEnvironmentalRestart: TryCompleteRac1EnvironmentalRestart,
        RefreshHud: feedback => RefreshRac1HudState(feedback), Hand: () => Rac1HandPresentation,
        SceneKind: () => _sceneKind, Diagnostics: () => Diagnostics);

    private ApplicationDiagnostics? _diagnostics;
    private ApplicationDiagnostics Diagnostics => _diagnostics ??= new(Components);
    private CaptureComposition? _capture;
    private CaptureComposition Capture => _capture ??= new(Components);
    private Rac1WorldComposition? _rac1Composition;
    private Rac1WorldComposition Rac1Composition => _rac1Composition ??= new(Components);
    private Rac2WorldComposition? _rac2Composition;
    private Rac2WorldComposition Rac2Composition => _rac2Composition ??= new(Components);

    internal Task CheckControllerWeaponInputAsync() => Diagnostics.CheckControllerWeaponInputAsync();

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

    public EquipmentChangeResult ResetPlacedActor(string id) => ActorHost.ResetPlacement(id);

    private void BuildScene(string scene)
    {
        _mode = Mode.Smoke;
        _sceneKind = scene;
        GodotSmokeSceneFixture.BuildApplicationScene(this, Presentation, _hudState, scene);
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
        _hostOperations.Start("stress-switch", token => Diagnostics.CreateStressSwitchCliService().RunStressSwitchAsync(sequence, token),
            lifetime: CancellationToken.None);
    public void OpenRac1CampaignCurrentFromBootstrap()
    {
        Sources.Initialize();
        OpenDestinationFromBootstrap($"rac1:LEVEL{_rac1Campaign.Session.Campaign.CurrentLevel}");
    }

    private void ShowTitleScreen() => _navigation.ShowTitle();
    private void ShowDestinationSelector() => _navigation.ShowSelector();
    private bool OnDestinationChosen(ObpDestination destination) => _navigation.Choose(destination);
    private bool EnterProviderWorld(string path, ObpDestination destination) =>
        _navigation.EnterProviderWorld(path, destination);
    private bool EnterFusionLab() => _navigation.EnterLab();
    private void ClearFusionLabStatus() => _navigation.ClearLab();

    private void AbandonFailedRac1Travel() => _rac1Campaign.AbandonUncommittedTravel();

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
            SelectNativeWeapon: Rac1CombatInput.Select,
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
            SetPlayerAudioPaused: paused => Rac1WeaponUsePresentationHost.SetPlayerAudioPaused(paused, _playerAudio, _equipmentPresentation, FusionWeapons),
            TryMissionInteraction: input => _worldScenes?.Current?.Resources.Rac1Gameplay?.Missions.TryHandleInteraction(input) == true,
            TryTaxiInteraction: input => (_worldScenes?.Current?.Resources.Rac1Gameplay?.Taxis)?.TryHandleInput(input) == true,
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

    private PauseMenuUi CreatePauseMenuPresentation() => PauseMenuUi.Create(new(
        Equipment: Modals.Equipment, Ordinary: () => OrdinaryPlayerSession, Player: () => _player,
        Inventory: () => _rac1Weapons, Native: () => _playerNative,
        Avatar: () => _avatarPresentation, Actors: () => ActorHost,
        ProfileStatus: () => _profileStatus, SaveNotice: () => PlayerSaveNotice,
        ResetActor: ResetPlacedActor, Resources: () => _worldScenes?.Current?.Resources,
        Debug: () => _debugOutputEnabled, Preview: () => _persistentPlayer.WeaponPreview is not null,
        Fusion: () => FusionWeapons, Close: ClosePauseMenu,
        Lab: () => { EnterFusionLab(); }, OpenSettings: () => Modals.OpenSettings(fromPause: true),
        SetDebug: SetDebugOutputEnabled, SaveMovement: () => Capture.Reproduction.SaveMovementState(),
        LoadMovement: () => Capture.Reproduction.StartMovementStateLoad(Capture.Reproduction.MovementStatePath),
        RequestPreview: disabled => Rac1WeaponUsePresentationHost.RequestPreview(disabled, () => FusionWeapons,
            () => _worldScenes?.Current?.Resources.Rac1Gameplay, Capture.Reproduction.Report),
        Worlds: ReturnToWorldsFromPause, Quit: () => ApplicationLifecycle.RequestQuit(this, "pause-menu-quit")));

    private void SetDebugOutputEnabled(bool enabled)
    {
        Modals.DebugOutputEnabled = enabled;
        Presentation.SetDebugOutputEnabled(enabled, Capture.MovementNotices.Hide);
        UpdateWorldHud();
        UpdatePlayerHud();
    }

    private WeaponQuickSelectUi.Entry[] ReadWeaponQuickSelectEntries() => WeaponQuickSelectUi.ReadEntries(
        _rac1Weapons, _persistentPlayer.Rac1Use, _playerNative, _persistentPlayer.Equipment, () => FusionWeapons);

    // Persistent source campaign save policy is distinct from ordinary player-profile
    // persistence. The application only supplies its input mode, notifications and
    // the shared wallet selected by the current player entry policy.
    private string? _rac1CampaignRestoreWarning => _rac1Campaign.Persistence.RestoreWarning;
    private Rac1CampaignRestoreKind _rac1CampaignRestoreKind => _rac1Campaign.Persistence.RestoreKind;

    private void EnsureRac1CampaignPersistenceInitialized() => _rac1Campaign.Persistence.EnsureInitialized();
    private void TickRac1CampaignSaves(double delta) => _rac1Campaign.Persistence.TickAndReport(delta);
    private void FlushRac1CampaignSaves(string boundary) => _rac1Campaign.Persistence.FlushAndReport(boundary);
    private void PersistRac1CampaignState(string reason) => _rac1Campaign.Persistence.Persist(reason);

    // Source-world retry/environment callbacks resolve the current lease only.
    // Native player life and campaign checkpoint authority remain separate.
    private bool TryCompleteRac1EnvironmentalRestart(bool automatic) =>
        _worldScenes?.Current?.Resources.Rac1Gameplay?.Lifecycle.TryCompleteEnvironmentalRestart(automatic) == true;

    private void TickRac1VeldinEnvironmentalDeath() =>
        _worldScenes?.Current?.Resources.Rac1Gameplay?.Lifecycle.TickEnvironmentalDeath();

    private void ShowRac1CombatRetry() =>
        _worldScenes?.Current?.Resources.Rac1Gameplay?.Lifecycle.ShowCombatRetry();

    /// <summary>
    /// Feed a recovered progression-dispatch value into the destination admission
    /// branch. No host location, objective, or completion trigger is inferred.
    /// </summary>
    public Rac1DestinationDiscoveryResult ApplyRac1CampaignProgressionEvent(
        int progressionEventId)
    {
        Sources.Initialize();
        EnsureRac1CampaignPersistenceInitialized();
        return _rac1Campaign.ApplyProgressionEvent(progressionEventId, PersistRac1CampaignState);
    }

    /// <summary>
    /// Apply an already-identified native item-acquisition event. This is the
    /// recovered common acquisition prefix only; vendor price/payment and the
    /// unresolved quick-select insertion predicate stay outside this boundary.
    /// </summary>
    public Rac1ItemAcquisitionResult ApplyRac1ItemAcquisition(int nativeItemId)
    {
        Sources.Initialize();
        EnsureRac1CampaignPersistenceInitialized();

        return _persistentPlayer.Equipment.AcquireNativeItem(nativeItemId, _rac1Campaign.Session.Weapons,
            capabilities => { if (_player is { } player) player.Rac1MovementEquipment = capabilities; },
            PersistRac1CampaignState, feedback => RefreshRac1HudState(feedback));
    }

    /// <summary>
    /// Apply an already-recovered native ammo amount through the generic retail
    /// clamp helper. Selection of which pickup refills which item, and by how
    /// much, remains a separate class-511 recovery problem.
    /// </summary>
    public Rac1AmmoGrantResult ApplyRac1AmmoGrant(int nativeItemId, int amount)
    {
        Sources.Initialize();
        EnsureRac1CampaignPersistenceInitialized();

        return _persistentPlayer.Equipment.GrantNativeAmmo(nativeItemId, amount, _rac1Campaign.Session.Weapons,
            PersistRac1CampaignState, feedback => RefreshRac1HudState(feedback));
    }

    // World lifetime and activation composition; policy and scene ownership live in the bound owners.
    private WorldSceneLifetime<GodotWorldResources>? _worldScenes;
    private WorldSceneLifetime<GodotWorldResources> WorldScenes => _worldScenes ??=
        new(RetireWorldBindings, ActivateOwnedWorld, () => _worldEntries?.Unload());
    private RuntimeSpawn? _worldPlayerEntry => _worldScenes?.PlayerEntry;
    private float _worldEntrySnapReach => _worldScenes?.EntrySnapReach ?? 8;

    private bool EnterWorld(string destinationId, Func<RuntimeWorld> load, ObpDestination? nativeDestination,
        string? sourcePath, FusionLabDefinition? lab = null) => _navigation.EnterPreparedWorld(destinationId,
        () => WorldScenes.Enter(destinationId, WorldEntryPreparationPolicy.CreateWorldSource(load), nativeDestination,
            sourcePath, lab, new(
                PreparePlayer: PrepareSelectedPlayerAssets,
                PrepareControllers: () => Capture.Replay.PrepareControllers(() => { _playerNative!.ResetReplayControllers(); FusionWeapons.BindPersistent(_playerNative.Weapons); }),
                Session: () => _worldEntries!, Mode: () => PlayerEntryMode,
                Avatars: PlayerAvatarPresentationHost.Registry,
                SnapReach: WorldEntryPreparationPolicy.SourceEntrySnapReach,
                PrepareScene: (world, id) => new GodotWorldResources(WorldHost.PrepareScene(world, id, WorldEntryPreparationPolicy.WorldOptions(world, _args))),
                AdmitActivation: () => Sources.RequireOrdinary("world activation"))),
        () =>
        {
            _lastCommittedProfileDestination = destinationId;
            var diagnostics = new WorldSmokeHarness.Factories(
                        Diagnostics.CreateRac1EncounterDiagnosticHarness, Diagnostics.CreateRac1CampaignDiagnosticHarness,
                        Diagnostics.CreateMovementDiagnosticHarness, Diagnostics.CreateRac1LootDiagnosticHarness,
                        Diagnostics.CreateWeaponDiagnosticHarness, Diagnostics.CreateCaptureDiagnosticHarness,
                        Diagnostics.CreateRac1WorldRouteDiagnosticHarness, Diagnostics.CreatePlayerDiagnosticHarness,
                        definition => new Rac3RatchetSeedScenario(definition,
                            new GodotRac3RatchetSeedFixture(_worldScenes?.Current?.Resources.Rac3Gameplay, () => _player).Sample,
                            message => GD.Print(message)));
            GodotWorldSmokeDispatch.Start(new(this, _args, nativeDestination, _worldSwitches, diagnostics,
                Sources.Setup.Access != SourceAccessMode.ExplicitDiagnostic, Sources.Setup.Library,
                new GodotDiagnosticWorldObservations(WorldScenes).ReadWorld, _hostOperations,
                (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code), message => GD.PrintErr(message)));
            Capture.Verification.StartForWorldEntry(nativeDestination, _args.VerifyHash, _worldSwitches);
            SaveSharedProfile("world transition");
            WorldSmokeHarness.StartEntryChecks(_args, _worldSwitches, diagnostics,
                Diagnostics.CreateUiDiagnosticHarness, Diagnostics.CreateFusionWeaponDiagnosticHarness, () => Capture.Replay.WorldEntered(destinationId));
        },
        () => { _avatarPresentation.DiscardPrepared(); _equipmentPresentation.DiscardPrepared(); });

    private void ActivateOwnedWorld(WorldSceneLease<GodotWorldResources> entry) => entry.Resources.Activate(entry, new(
        Host: this, Application: Presentation, Args: () => _args,
        Avatar: _avatarPresentation, Equipment: _equipmentPresentation, Ordinary: () => OrdinaryPlayerSession,
        SetAvatarStamp: stamp => _avatarSourceStamp = stamp,
        PlayerLife: PlayerLife, Navigation: _navigation, Sources: Sources,
        CreateRac1: Rac1Composition.CreateRac1WorldModule, CreateRac2: Rac2Composition.CreateRac2WorldModule,
        SpawnPlayer: SpawnPlayer, Actors: ActorHost, Campaign: _rac1Campaign,
        EquipmentWorld: FusionWeapons.BindWorld, CountActivation: WorldScenes.CountActivation,
        OverrideEntry: WorldScenes.OverrideAuthoredEntry,
        SetWorldMode: () => { _mode = Mode.World; _sceneKind = entry.Lab is null ? "destination" : "fusion-lab"; },
        Persist: PersistRac1CampaignState, Hud: _hudState,
            Effects: new(
            Camera: () => GetViewport().GetCamera3D(),
            Sources: () => Sources.Setup.Library.Attached.Values.ToArray(),
            ActorAlive: key => ActorHost.IsAlive(key),
            PlacementOffset: () => entry.Lab is not null ? System.Numerics.Vector3.Zero :
                WorldEffectHost.Runtime((_player?.GlobalPosition ?? Vector3.Zero) + Rac1WeaponUsePresentationHost.EquipmentForward(_player) * 6 + Vector3.Up * 8) - new System.Numerics.Vector3(0, 0, 18),
            Suspended: () => Capture.Reproduction.IsLoading || Capture.ReplayIfStarted?.Recorder is { WaitingForEntry: true } or { PreparingWorld: true },
            RecordOperation: operation => Capture.Replay.RecordCommand(new("effects", operation)),
            CommandAllowed: () => Capture.ReplayIfStarted?.Recorder is null || EffectCaptureContract.Includes(_args, Capture.ReplayIfStarted!.Recorder!.Reader?.Header),
            LogError: message => GD.PrintErr(message)),
            Ticks: new(
            MovementSuspended: () => Capture.Reproduction.IsLoading,
            ReplayWaitingForEntry: () => Capture.ReplayIfStarted?.Recorder is { WaitingForEntry: true },
            PlayerDead: () => _player?.Rac1GameplayAlive == false,
            PlayerReactions: () => _playerAudio?.StepReactions(),
            ActorPhysics: _ =>
            {
                if (_player is { } player)
                    ActorHost.Step(ToNumerics(player.GlobalPosition), PlayerLife.Alive, Capture.Reproduction.IsLoading);
            },
            ScenePresentation: delta => _worldHost.Tick(delta, _activeCamera?.GlobalPosition ?? Vector3.Zero),
            HudPresentation: () => { UpdatePlayerHud(); UpdateWorldHud(); }),
        EffectsSmoke: switches => GodotEffectsSmoke.StartIfRequested(_args, switches, () => new(
            this, Sources.Setup.Library, new GodotDiagnosticWorldObservations(WorldScenes).ReadWorld, _hostOperations,
            () => _worldScenes?.Current?.Resources.Effects,
            () => _worldScenes?.Current?.Resources.Scene.Root,
            () => _worldScenes?.Current?.Resources.EffectsFailure,
            () => System.Text.Json.JsonSerializer.Serialize(new
            {
                Configuration = _playerSession?.Capture().Configuration,
                Loadout = _playerSession?.Capture().Loadout,
                Actors = ActorHost.ReadActors().Select(actor => new { actor.Registration.Instance, actor.State.Health }).ToArray(),
            }),
            id => id == "obp:fusion-lab" ? EnterFusionLab() :
                TrilogyWorldProviders.Resolve(id) is { } next && OnDestinationChosen(next),
            (reason, code) => ApplicationLifecycle.RequestQuit(this, reason, code),
            message => GD.Print(message), message => GD.PrintErr(message))),
        PublishSummary: summary => _navigation.LoadSummary = summary,
        PlayerGeneration: () => _playerSession!.Capture().Generation));

    private void PrepareSelectedPlayerAssets()
    {
        EnsurePlayerSession();
        RefreshPlayerSourceAvailability();
        _persistentPlayer.ResolvePresentationProfiles();
        _avatarPresentation.PrepareSelectedWorldEntry(_playerSession!, _playerAnimationProfile!,
            Engine.PhysicsTicksPerSecond, Sources.Setup.VerifySelectedSourceFile,
            _equipmentPresentation, OrdinaryPlayerSession);
    }

    /// <summary>
    /// Bind the staged, session-selected presentation independently of destination.
    /// Missing required assets fail entry; the capsule is an explicit diagnostic.
    /// </summary>

    private void AttachPlayerAvatarVisual(PlayerHost player) => _avatarPresentation.AttachWorldVisual(player, new(
        Session: () => _playerSession, AnimationProfile: () => _playerAnimationProfile!,
        World: () => _world!, Destination: () => _activeDestination, Scene: () => _sceneResult,
        Gameplay: () => _worldScenes?.Current?.Resources.Rac1Gameplay,
        Hand: () => Rac1HandPresentation, Player: () => _player,
        Equipment: _equipmentPresentation, Use: _persistentPlayer.Rac1Use,
        EquipmentSources: () => _persistentPlayer.Equipment.Sources,
        Weapons: () => FusionWeapons, CombatInput: () => Rac1CombatInput,
        Alive: () => PlayerLife.Alive, Ordinary: () => OrdinaryPlayerSession,
        Audio: () => _playerAudio, MovementLoading: () => Capture.Reproduction.IsLoading));

    private int? CurrentPlayerAvatarSourceSequence() => _avatarPresentation.CurrentSourceSequence();
    private bool PlayerAvatarPresentationIsSynchronized() => _avatarPresentation.IsSynchronized;

    private void ClearPlayerAvatarView() => _avatarPresentation.ClearWorldVisual(_playerAudio, _equipmentPresentation);
}
