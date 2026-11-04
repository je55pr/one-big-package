using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.Game.Diagnostics;
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
/// Views over <see cref="ApplicationComponents"/> that more than one composition module
/// needs. Composition modules are composition roots: they bind owned components to narrow
/// ports and may read, and in a few named places act on, the application. This base is not
/// read-only. Gameplay classes, fixtures and scenarios never see it; they receive the narrow
/// ports a module builds. <c>CompositionBaseArchitectureTests</c> pins the permitted derived
/// types, forbids use outside composition files and ratchets the shared surface so it can
/// only shrink. A member needed by one module belongs in that module, not here.
/// </summary>
internal abstract class CompositionBase(ApplicationComponents components)
{
    protected ApplicationComponents Context { get; } = components;
    protected Node3D _host => Context.Host;
    protected CommandLineArgs _args => Context.Args();
    protected HostOperationScope _hostOperations => Context.Operations;
    protected PlayerSessionHost _persistentPlayer => Context.Player;
    protected Rac1CampaignHost _rac1Campaign => Context.Campaign;
    protected PlayerAvatarPresentationHost _avatarPresentation => Context.Avatar;
    protected PlayerEquipmentPresentationBinder _equipmentPresentation => Context.Equipment;
    protected HudStateAdapter _hudState => Context.Hud;
    protected GodotApplicationPresentation Presentation => Context.Presentation();
    protected WorldSceneLifetime<GodotWorldResources>? _worldScenes => Context.WorldScenesIfCreated();
    protected SourceSetupController Sources => Context.Sources();
    protected FusionWeaponHost FusionWeapons => Context.Fusion();
    protected GodotPlayerLifecycleHost PlayerLife => Context.Life();
    protected ActorWorldHost ActorHost => Context.Actors();
    protected MovementReproductionService MovementReproduction => Context.Reproduction();
    protected CaptureReplayCoordinator CaptureReplay => Context.Capture();
    protected CaptureReplayCoordinator? _captureReplay => Context.CaptureIfStarted();
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
    protected long _frame => Context.Frame();

    protected CanvasLayer _ui => Presentation.Ui;
    protected Camera3D _camera => Presentation.Camera;
    protected Camera3D _activeCamera { get => Presentation.ActiveCamera; set => Presentation.ActiveCamera = value; }
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

    protected Window GetWindow() => _host.GetWindow();
    protected SceneTree GetTree() => _host.GetTree();
    protected static bool IsInstanceValid(GodotObject? value) => GodotObject.IsInstanceValid(value);

    protected bool EnterFusionLab() => _navigation.EnterLab();
    protected bool OnDestinationChosen(ObpDestination destination) => _navigation.Choose(destination);
    protected void OpenDestinationFromBootstrap(string id) => Context.OpenDestination(id);
    protected bool TryCompleteRac1EnvironmentalRestart(bool automatic) => Context.CompleteEnvironmentalRestart(automatic);
    protected int? CurrentPlayerAvatarSourceSequence() => _avatarPresentation.CurrentSourceSequence();
    protected IReadOnlyList<EquipmentCatalogEntry> ReadPlayerEquipment() => _persistentPlayer.Equipment.Read();
    protected EquipmentChangeResult SelectPlayerEquipment(string catalogId) => _persistentPlayer.Equipment.Select(catalogId);
    protected EquipmentChangeResult AssignPlayerQuickSelect(int slot, string? catalogId) => _persistentPlayer.Equipment.AssignQuickSelect(slot, catalogId);
    protected EquipmentChangeResult UnequipPlayerEquipment() => _persistentPlayer.Equipment.Unequip();
    protected EquipmentChangeResult SetPlayerGadgetEnabled(string catalogId, bool enabled) => _persistentPlayer.Equipment.SetGadgetEnabled(catalogId, enabled);
    protected Rac1HandPresentationContext Rac1HandPresentation => Context.Hand();
    protected bool ControllerSettingsOpen => Modals.SettingsOpen;
    protected bool Rac1PlanetMapOpen => _navigation.PlanetMapOpen;
    protected Viewport GetViewport() => _host.GetViewport();
    protected void RefreshRac1HudState(params HudFeedbackDraft[] feedback) => Context.RefreshHud(feedback);
}
