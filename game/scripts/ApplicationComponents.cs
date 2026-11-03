using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC1.Progression;
using OBP.Fusion;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OBP.Game;

/// <summary>
/// The complete set of application facts and actions that diagnostic fixtures may be
/// composed from. Components are the owners themselves; everything the root computes
/// (projections, mode, navigation verbs) is a named delegate. Fixtures receive narrow
/// ports built from these, never the root node, so a new scenario is registered in
/// <see cref="ApplicationDiagnostics"/> without touching <c>OBPGame</c>.
/// </summary>
internal sealed record ApplicationComponents(
    Node3D Host,
    Func<CommandLineArgs> Args,
    Action<CommandLineArgs> ReplaceArgs,
    HostOperationScope Operations,
    PlayerSessionHost Player,
    Rac1CampaignHost Campaign,
    PlayerAvatarPresentationHost Avatar,
    PlayerEquipmentPresentationBinder Equipment,
    HudStateAdapter Hud,
    Func<GodotApplicationPresentation> Presentation,
    Func<WorldSceneLifetime<GodotWorldResources>> WorldScenes,
    Func<SourceSetupController> Sources,
    Func<FusionWeaponHost> Fusion,
    Func<GodotPlayerLifecycleHost> Life,
    Func<ActorWorldHost> Actors,
    Func<MovementReproductionService> Reproduction,
    Func<CaptureReplayCoordinator> Capture,
    Func<CaptureReplayCoordinator?> CaptureIfStarted,
    Func<GodotCapturePresentation> CapturePresentation,
    Func<ApplicationNavigationCoordinator> Navigation,
    Func<UiModalCoordinator> Modals,
    Func<BugReportUiCoordinator?> Bugs,
    Func<ProfileCoordinator> Profiles,
    Func<Rac1PlayerAudioPresenter?> PlayerAudio,
    Func<IRac1WeaponPlayerInput> CombatInput,
    Func<ObpPlayerSession?> PlayerSession,
    Func<PlayerSessionNativeState?> PlayerNative,
    Func<bool> OrdinaryPlayer,
    Func<Rac1WeaponInventory> Rac1Weapons,
    Func<Rac1HeldItemSession> Rac1Held,
    Func<Rac1BoltWallet> Rac1Wallet,
    Func<Rac1RatchetNanotechSession> Rac1Nanotech,
    Func<SourceWeaponKey> CurrentWeapon,
    WorldHost IdleWorldHost,
    Func<(bool Title, bool Selector, bool World, bool Picker)> Mode,
    Func<long> Frame,
    Action<string> OpenDestination,
    Func<string, Func<RuntimeWorld>, ObpDestination?, string?, bool> EnterWorld,
    Action RefreshPlayerSources,
    Func<int, Rac1DestinationDiscoveryResult> ApplyProgressionEvent,
    Func<bool, bool> CompleteEnvironmentalRestart,
    Action<HudFeedbackDraft[]> RefreshHud,
    Func<Rac1HandPresentationContext> Hand);
