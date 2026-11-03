using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.RAC1.Progression;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;
using OBP.Runtime.Presentation;

namespace OBP.Game;

/// <summary>
/// Read-only views over <see cref="ApplicationComponents"/> shared by the composition
/// modules (diagnostics, source-world and capture bindings). Only composition code derives
/// from this; gameplay classes, fixtures and scenarios receive narrow ports, never these
/// views, and an architecture test pins the permitted derived types.
/// </summary>
internal abstract class CompositionBase(ApplicationComponents components)
{
    protected ApplicationComponents Context { get; } = components;
    protected Node3D _host => Context.Host;
    protected CommandLineArgs _args { get => Context.Args(); set => Context.ReplaceArgs(value); }
    protected HostOperationScope _hostOperations => Context.Operations;
    protected PlayerSessionHost _persistentPlayer => Context.Player;
    protected Rac1CampaignHost _rac1Campaign => Context.Campaign;
    protected PlayerAvatarPresentationHost _avatarPresentation => Context.Avatar;
    protected PlayerEquipmentPresentationBinder _equipmentPresentation => Context.Equipment;
    protected HudStateAdapter _hudState => Context.Hud;
    protected GodotApplicationPresentation Presentation => Context.Presentation();
    protected WorldSceneLifetime<GodotWorldResources> WorldScenes => Context.WorldScenes();
    protected WorldSceneLifetime<GodotWorldResources>? _worldScenes => Context.WorldScenes();
    protected SourceSetupController Sources => Context.Sources();
    protected FusionWeaponHost FusionWeapons => Context.Fusion();
    protected GodotPlayerLifecycleHost PlayerLife => Context.Life();
    protected ActorWorldHost ActorHost => Context.Actors();
    protected MovementReproductionService MovementReproduction => Context.Reproduction();
    protected CaptureReplayCoordinator CaptureReplay => Context.Capture();
    protected CaptureReplayCoordinator? _captureReplay => Context.CaptureIfStarted();
    protected GodotCapturePresentation CapturePresentation => Context.CapturePresentation();
    protected ApplicationNavigationCoordinator _navigation => Context.Navigation();
    protected UiModalCoordinator Modals => Context.Modals();
    protected BugReportUiCoordinator? _bugReporting => Context.Bugs();
    protected ProfileCoordinator Profiles => Context.Profiles();
    protected Rac1PlayerAudioPresenter? _playerAudio => Context.PlayerAudio();
    protected IRac1WeaponPlayerInput Rac1CombatInput => Context.CombatInput();
    protected ObpPlayerSession? _playerSession => Context.PlayerSession();
    protected PlayerSessionNativeState? _playerNative => Context.PlayerNative();
    protected bool OrdinaryPlayerSession => Context.OrdinaryPlayer();
    protected Rac1WeaponInventory _rac1Weapons => Context.Rac1Weapons();
    protected Rac1HeldItemSession _rac1HeldItem => Context.Rac1Held();
    protected Rac1BoltWallet _rac1BoltWallet => Context.Rac1Wallet();
    protected Rac1RatchetNanotechSession _rac1Nanotech => Context.Rac1Nanotech();
    protected SourceWeaponKey CurrentWeaponKey => Context.CurrentWeapon();
    protected long _frame => Context.Frame();

    protected Node3D _worldRoot => Presentation.WorldRoot;
    protected Node3D _playerRoot => Presentation.PlayerRoot;
    protected CanvasLayer _ui => Presentation.Ui;
    protected Camera3D _camera => Presentation.Camera;
    protected Camera3D _activeCamera { get => Presentation.ActiveCamera; set => Presentation.ActiveCamera = value; }
    protected PlayerHud? _playerHud => Presentation.PlayerHud;
    protected PlayerHost? _player => Presentation.Player;
    protected WorldHost _worldHost => _worldScenes?.Current?.Resources.Presentation ?? Context.IdleWorldHost;
    protected RuntimeWorld? _world => _worldScenes?.Current?.Plan.Prepared.World;
    protected RuntimeWorldScene.Result? _sceneResult => _worldScenes?.Current?.Resources.Scene;
    protected int _worldSwitches => _worldScenes?.ActivationCount ?? 0;
    protected ObpDestination? _activeDestination => _worldScenes?.NativeDestination;
    protected string? _currentDestinationId => _worldScenes?.DestinationId;
    protected Rac1WorldModule Rac1World => _worldScenes?.Current?.Resources.Rac1Gameplay
        ?? throw new InvalidOperationException("R&C1 gameplay requires an active scene lease.");

    protected PauseMenuUi? _pauseMenu => Modals.PauseMenu;
    protected WeaponQuickSelectUi? _quickSelect => Modals.QuickSelect;
    protected bool PauseMenuOpen => Modals.PauseOpen;
    protected bool QuickSelectOpen => Modals.QuickSelectOpen;
    protected bool BugReportModalOpen => _bugReporting?.IsOpen == true || _captureReplay?.Recorder?.BugInputSuspended == true;
    protected string? SharedProfilePath => Profiles.SharedProfilePath;
    protected ObpProfile? _profileDocument => Profiles.Document;
    protected bool _profileWriteBlocked => Profiles.WriteBlocked;
    protected string? _rac1CampaignRestoreWarning => _rac1Campaign.Persistence.RestoreWarning;
    protected Rac1CampaignRestoreKind _rac1CampaignRestoreKind => _rac1Campaign.Persistence.RestoreKind;
    protected string? PlayerEquipmentConfigurationIdentity => _persistentPlayer.Equipment.ConfigurationIdentity;

    protected Window GetWindow() => _host.GetWindow();
    protected SceneTree GetTree() => _host.GetTree();
    protected static bool IsInstanceValid(GodotObject? value) => GodotObject.IsInstanceValid(value);

    protected bool EnterFusionLab() => _navigation.EnterLab();
    protected bool OnDestinationChosen(ObpDestination destination) => _navigation.Choose(destination);
    protected bool EnterProviderWorld(string path, ObpDestination destination) => _navigation.EnterProviderWorld(path, destination);
    protected void ShowTitleScreen() => _navigation.ShowTitle();
    protected void ShowDestinationSelector() => _navigation.ShowSelector();
    protected bool TryHandleInteractiveBack() => _navigation.HandleBack(Modals);
    protected bool TryOpenPauseMenu() => Modals.OpenPause();
    protected bool TryClosePauseMenu() => Modals.TryClosePause();
    protected bool TryOpenWeaponQuickSelect(InputEvent input) => Modals.OpenQuickSelect(input);
    protected void CloseWeaponQuickSelect(SourceWeaponKey? weapon = null) => Modals.CloseQuickSelect(weapon);
    protected void OpenDestinationFromBootstrap(string id) => Context.OpenDestination(id);
    protected bool EnterWorld(string destinationId, Func<RuntimeWorld> load, ObpDestination? nativeDestination, string? sourcePath) =>
        Context.EnterWorld(destinationId, load, nativeDestination, sourcePath);
    protected void RefreshPlayerSourceAvailability() => Context.RefreshPlayerSources();
    protected void RememberCommandLineSource(ObpSourceGame game, string path, bool persist = true) => Sources.Remember(game, path, persist);
    protected Rac1PlanetTravelStartResult TravelRac1CampaignTo(int id) => _navigation.TravelRac1CampaignTo(id);
    protected Rac1DestinationDiscoveryResult ApplyRac1CampaignProgressionEvent(int id) => Context.ApplyProgressionEvent(id);
    protected void TickRac1CampaignSaves(double delta) => _rac1Campaign.Persistence.TickAndReport(delta);
    protected void FlushRac1CampaignSaves(string boundary) => _rac1Campaign.Persistence.FlushAndReport(boundary);
    protected bool TryCompleteRac1EnvironmentalRestart(bool automatic) => Context.CompleteEnvironmentalRestart(automatic);
    protected bool SaveSharedProfile(string reason) => Profiles.Save(reason);
    protected ObpProfile CaptureSharedProfile() => Profiles.Capture();
    protected IReadOnlyList<ProfileSource> CaptureProfileSources() => Profiles.CaptureProfileSources();
    protected void ApplyOverlaySpec(string? spec) => Presentation.ApplyOverlaySpec(spec);
    protected void UpdateWorldHud() => Presentation.UpdateWorldHud(_worldScenes?.Current, _worldHost, _worldSwitches, Context.Mode().World);
    protected int? CurrentPlayerAvatarSourceSequence() => _avatarPresentation.CurrentSourceSequence();
    protected bool PlayerAvatarPresentationIsSynchronized() => _avatarPresentation.IsSynchronized;
    protected IReadOnlyList<EquipmentCatalogEntry> ReadPlayerEquipment() => _persistentPlayer.Equipment.Read();
    protected EquipmentChangeResult SelectPlayerEquipment(string catalogId) => _persistentPlayer.Equipment.Select(catalogId);
    protected EquipmentChangeResult AssignPlayerQuickSelect(int slot, string? catalogId) => _persistentPlayer.Equipment.AssignQuickSelect(slot, catalogId);
    protected EquipmentChangeResult UnequipPlayerEquipment() => _persistentPlayer.Equipment.Unequip();
    protected EquipmentChangeResult SetPlayerGadgetEnabled(string catalogId, bool enabled) => _persistentPlayer.Equipment.SetGadgetEnabled(catalogId, enabled);
    protected static System.Numerics.Vector3 ToNumerics(Vector3 value) => new(value.X, value.Y, value.Z);
    protected Rac1HandPresentationContext Rac1HandPresentation => Context.Hand();
    protected bool InWorldMode => Context.Mode().World;
    protected bool ControllerSettingsOpen => Modals.SettingsOpen;
    protected bool Rac1PlanetMapOpen => _navigation.PlanetMapOpen;
    protected SourceWeaponKey? _previousSelectedWeapon { get => Modals.PreviousSelectedWeapon; set => Modals.PreviousSelectedWeapon = value; }
    protected ulong _lastQuickSelectTap { set { if (value == 0) Modals.ResetWheelGesture(); } }
    protected Viewport GetViewport() => _host.GetViewport();
    protected void AddChild(Node node) => _host.AddChild(node);
    protected void RefreshRac1HudState(params HudFeedbackDraft[] feedback) => Context.RefreshHud(feedback);
    protected void PersistRac1CampaignState(string reason) => _rac1Campaign.Persistence.Persist(reason);
    protected void TickRac1VeldinEnvironmentalDeath() => _worldScenes?.Current?.Resources.Rac1Gameplay?.Lifecycle.TickEnvironmentalDeath();
    protected void ShowRac1CombatRetry() => _worldScenes?.Current?.Resources.Rac1Gameplay?.Lifecycle.ShowCombatRetry();
}
