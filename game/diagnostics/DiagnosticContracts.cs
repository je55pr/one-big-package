using System.Collections.Immutable;
using System.Numerics;
using OBP.Core;
using OBP.Fusion;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;
using OBP.Runtime.Camera;
using OBP.Runtime.Presentation;

namespace OBP.Game.Diagnostics;

/// <summary>Process/Physics are the SceneTree frame-start signals, before node callbacks. PostDraw is the rendering signal itself.</summary>
public enum DiagnosticFramePhase { Process, Physics, PostDraw }
public enum DiagnosticSourceAccess { AssetFree, ExplicitSourceDiagnostic, OrdinaryPlay }
public enum DiagnosticSetup { AssetFreeFixture, ExistingWorld, FreshWorld }
public enum DiagnosticRunStatus { Passed, Failed, Canceled, TimedOut, Unavailable }
public sealed record DiagnosticSourceRequirement(ObpSourceGame Game, string Build);
public sealed record DiagnosticSourceSnapshot(ObpSourceGame Game, string Build, bool Available, string? Reason = null);

/// <summary>Explicit test metadata. It never chooses the ordinary player's source/provider from a destination.</summary>
public sealed record DiagnosticScenarioDefinition(string Id, DiagnosticSourceAccess SourceAccess, DiagnosticSetup Setup,
    ImmutableArray<DiagnosticSourceRequirement> RequiredSources, TimeSpan Timeout, int MaximumFrameWaits,
    string? DestinationId = null, int FailureExitCode = 1)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || !Enum.IsDefined(SourceAccess) || !Enum.IsDefined(Setup) ||
            RequiredSources.IsDefault || Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromHours(1) || MaximumFrameWaits <= 0 || FailureExitCode <= 0)
            throw new ArgumentException("Invalid diagnostic scenario identity, bounds or failure status.");
        if (RequiredSources.Any(source => source.Game is not (ObpSourceGame.Rac1 or ObpSourceGame.Rac2 or ObpSourceGame.Rac3) || string.IsNullOrWhiteSpace(source.Build)) ||
            RequiredSources.Select(source => source.Game).Distinct().Count() != RequiredSources.Length)
            throw new ArgumentException("Diagnostic sources require distinct source/build authorities.");
        if (SourceAccess == DiagnosticSourceAccess.AssetFree && (RequiredSources.Length != 0 || Setup != DiagnosticSetup.AssetFreeFixture) ||
            SourceAccess == DiagnosticSourceAccess.ExplicitSourceDiagnostic && RequiredSources.Length == 0 ||
            Setup != DiagnosticSetup.AssetFreeFixture && string.IsNullOrWhiteSpace(DestinationId) ||
            SourceAccess == DiagnosticSourceAccess.OrdinaryPlay &&
                !new[] { ObpSourceGame.Rac1, ObpSourceGame.Rac2, ObpSourceGame.Rac3 }.All(game => RequiredSources.Any(source => source.Game == game)))
            throw new ArgumentException("Asset-free fixtures and ordinary trilogy play require explicit, separate admission.");
    }
}

public sealed record DiagnosticRunResult(string Id, DiagnosticRunStatus Status, int ExitCode, string Detail, Exception? Error = null);
public sealed class DiagnosticUnavailableException(string message) : Exception(message);
public sealed record DiagnosticFrameSnapshot(ulong Process, ulong Physics);
public sealed record DiagnosticSceneSnapshot(bool RootLoaded, bool SourceBootstrapPresent, bool UiPresent, bool SourceManagerPresent);
public sealed record DiagnosticWorldSnapshot(string? DestinationId, WorldContentIdentity? Content, string? WorldInstanceId, long Generation);
public sealed record DiagnosticPlayerSnapshot(PlayerAvatarIdentity? Avatar, Vector3 Position, Vector3 Velocity, bool Grounded, long NativeTick, int? AnimationSequence, double? Health);
public sealed record DiagnosticPoseSnapshot(PlayerAvatarIdentity Avatar, long NativeTick, ImmutableArray<PlayerJointChannels> Joints);
public sealed record DiagnosticHudSnapshot(long Epoch, long Revision, HudPresentationState State, ImmutableArray<HudFeedbackEvent> Feedback);
public sealed record DiagnosticCameraSnapshot(RuntimeCameraState State);
public sealed record DiagnosticUiSnapshot(string? ActiveMenuId, string? SelectedActionId, bool InputBlocked, ImmutableArray<string> VisibleActions);
public sealed record DiagnosticActorSnapshot(ActorInstanceKey Instance, FusionSourceIdentity Source, string AdapterId,
    string NativeContextId, ActorObservation Observation, ImmutableArray<ActorCapabilityRegistration> Capabilities);
public sealed record DiagnosticEquipmentSnapshot(SourceWeaponKey Item, int Ammo, bool Held, bool Available, string? Reason);
public sealed record DiagnosticAudioSnapshot(FusionSourceIdentity Source, long NativeTick, string Event, int Count);
public sealed record DiagnosticReplaySnapshot(bool BuildChanged, int CheckpointsCompared, string? Result);
public sealed record DiagnosticActionResult(bool Accepted, string? Reason);

public interface IDiagnosticSources { ImmutableArray<DiagnosticSourceSnapshot> ReadSources(); }
public interface IDiagnosticSceneObservations { DiagnosticSceneSnapshot ReadScene(); }
public interface IDiagnosticWorldObservations { DiagnosticWorldSnapshot ReadWorld(); }
public interface IDiagnosticPlayerObservations { DiagnosticPlayerSnapshot ReadPlayer(); }
public interface IDiagnosticPoseObservations { DiagnosticPoseSnapshot ReadPose(); }
public interface IDiagnosticHudObservations { DiagnosticHudSnapshot ReadHud(); }
public interface IDiagnosticCameraObservations { DiagnosticCameraSnapshot ReadCamera(); }
public interface IDiagnosticUiObservations { DiagnosticUiSnapshot ReadUi(); }
public interface IDiagnosticActorObservations { ImmutableArray<DiagnosticActorSnapshot> ReadActors(); }
public interface IDiagnosticEquipmentObservations { ImmutableArray<DiagnosticEquipmentSnapshot> ReadEquipment(); }
public interface IDiagnosticAudioObservations { ImmutableArray<DiagnosticAudioSnapshot> ReadAudio(); }
public interface IDiagnosticReplayEvidence { DiagnosticReplaySnapshot ReadEvidence(); }
public interface IDiagnosticNavigationActions { Task<DiagnosticActionResult> NavigateAsync(string destinationId, CancellationToken cancellationToken); }
public interface IDiagnosticUiActions { Task<DiagnosticActionResult> ActivateAsync(string actionId, CancellationToken cancellationToken); }

public interface IDiagnosticFrameClock
{
    DiagnosticFrameSnapshot ReadFrames();
    Task NextAsync(DiagnosticFramePhase phase, CancellationToken cancellationToken);
}

public interface IDiagnosticInputActions { IDiagnosticInputLease Acquire(CancellationToken lifetime); }
public interface IDiagnosticInputLease : IDisposable { void SetAction(string action, float strength); }

public interface IDiagnosticWorldLease : IAsyncDisposable { CancellationToken Lifetime { get; } }
public interface IDiagnosticWorldSetup
{
    Task<IDiagnosticWorldLease> PrepareAsync(string destinationId, bool fresh, DiagnosticSourceAccess sourceAccess, CancellationToken cancellationToken);
}

/// <summary>A scenario receives only the domain ports supplied to its own constructor.</summary>
public interface IDiagnosticScenario
{
    DiagnosticScenarioDefinition Definition { get; }
    Task RunAsync(DiagnosticRunContext run);
}
