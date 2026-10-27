using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Progression;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>
/// Host-composed facts and actions; navigation does not inspect privileged
/// OBPGame fields or take ownership of a world or player session.
/// </summary>
internal sealed record ApplicationNavigationPorts(
    Node3D Host,
    Func<SourceSetupController> Sources,
    Func<CommandLineArgs> Args,
    Func<WorldSceneLifetime<GodotWorldResources>> Scenes,
    Func<RuntimeWorld?> World,
    Func<RuntimeWorldScene.Result?> SceneResult,
    Func<CanvasLayer> Ui,
    Func<PlayerHost?> Player,
    Func<Rac1CampaignRuntimeSession> Campaign,
    Func<long> Frame,
    Func<bool> IsWorld,
    Func<bool> IsSelector,
    Func<bool> PauseMenuOpen,
    Func<bool> ControllerSettingsOpen,
    Func<bool> QuickSelectOpen,
    Func<bool> DebugOutput,
    Func<string?> SaveNotice,
    Func<string?> CurrentDestinationId,
    Func<bool> FusionPreparing,
    Func<string?> FusionFailure,
    Action CancelPreparation,
    Action AbandonFailedTravel,
    Action<Exception, bool> ReportFailure,
    Func<string, Func<RuntimeWorld>, ObpDestination?, string?, FusionLabDefinition?, bool> EnterWorld,
    Action<bool> EnterPage);
