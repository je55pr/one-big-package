using OBP.Fusion;
using OBP.Runtime.Diagnostics;

namespace OBP.Game;

internal sealed record PlaytestDriverPorts(GodotPlaytestHost Host, HostOperationScope Operations,
    Func<bool> OrdinaryPlayer, Func<bool> MovementLoading, Func<int?> BookmarkStep,
    PlaytestReportActions Reports, Func<string?> SourceBuild,
    Func<PlaytestWeaponConfiguration?> Weapons, Func<(string Id, string Label)> Destination,
    Func<MovementReproState> Bookmark, Action<string> MovementNotice, PlaytestDiagnosticActions Diagnostics);

internal sealed record PlaytestDiagnosticActions(Action<bool, CancellationToken> StartRecording,
    Action<CancellationToken> StartReports, Action<CancellationToken> StartReconciliation,
    Func<CancellationToken, Task> VerifyFinishedContinuation, Action<Exception> SaveFailed);

internal sealed record PlaytestReportActions(Func<BugReportEntry?> ReplayReport, Action<BugReportEntry> OpenForm, Action OpenInbox, Action Close);

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
