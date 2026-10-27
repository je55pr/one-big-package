using Godot;
using OBP.Godot;

namespace OBP.Game.Diagnostics;

/// <summary>Invocation-only phase/input compatibility for the remaining #341-#345 callers.</summary>
internal static class GodotRac1EncounterCompatibility
{
    internal static async Task WaitAsync(Node owner, Func<bool> condition, int maximum, string label, CancellationToken token)
    {
        token = token.CanBeCanceled ? token : LegacyDiagnosticCancellation.Current;
        for (int frame = 0; frame < maximum; frame++)
        {
            token.ThrowIfCancellationRequested();
            if (condition()) return;
            await GodotAsyncWait.SignalAsync(owner.GetTree(), SceneTree.SignalName.ProcessFrame, token);
        }
        throw new TimeoutException($"Timed out waiting for RAC1 smoke phase: {label}.");
    }
}
