using Godot;
using OBP.Fusion;
using OBP.Game.Diagnostics;
using OBP.Runtime.Diagnostics;

namespace OBP.Game;

internal sealed record PlaytestDriverPorts(Node Host, HostOperationScope Operations, Func<PlayerHost?> Player,
    Func<bool> OrdinaryPlayer, Func<bool> MovementLoading, Func<int?> BookmarkStep,
    Func<BugReportUiCoordinator?> Reports, Func<PauseMenuUi?> PauseMenu, Func<string?> SourceBuild,
    Func<PlaytestWeaponConfiguration?> Weapons, Func<(string Id, string Label)> Destination,
    Func<MovementReproState> Bookmark, Action<string> MovementNotice, Func<CaptureDiagnosticHarness> Diagnostics);

internal static class GodotCaptureEffects
{
    internal static CaptureEffectReadiness Prepare(GodotWorldResources? resources)
    {
        if (resources?.EffectsFailure is { } failure) return new(false, failure);
        var effects = resources?.Effects;
        effects?.Prepare();
        return new(effects?.Ready == true, effects?.Failure);
    }

    internal static CaptureReadiness Readiness(bool preparing, ActorWorldHost actors, bool playerAndWorld)
    {
        var snapshot = actors.ReadSnapshot();
        return new(preparing || snapshot.Preparing, snapshot.Problems.Select(p => p.Message).ToArray(),
            snapshot.Lab, snapshot.Placed, snapshot.Expected, playerAndWorld);
    }
}
