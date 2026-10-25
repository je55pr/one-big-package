using OBP.Runtime.Diagnostics;

namespace OBP.Fusion;

/// <summary>Pre-step world commands run once, before the host's readiness barrier, regardless of callback phase.</summary>
public sealed class ReplayWorldTransitionStage
{
    private int _lastStagedStep = -1;

    /// <returns>True when commands were applied; the host must yield before consuming this step.</returns>
    public bool Stage(PlaytestStep step, Action<PlaytestCommand> apply)
    {
        if (step.Index == _lastStagedStep || !step.Commands.Any(command => !command.After && command.Kind == "destination")) return false;
        _lastStagedStep = step.Index;
        foreach (var command in step.Commands.Where(command => !command.After)) apply(command);
        return true;
    }
}
