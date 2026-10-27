using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;

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
    internal static async Task TapPrimaryAsync(Node owner, Rac1WeaponId selected)
    {
        var action = selected == Rac1WeaponId.FirstRanged ? RawGamepadInput.Traversal : RawGamepadInput.Action;
        Input.ActionPress(action);
        try { await GodotMovementCompatibility.FramesAsync(owner, 2, LegacyDiagnosticCancellation.Current); }
        finally { Input.ActionRelease(action); }
        await GodotMovementCompatibility.FramesAsync(owner, 2, LegacyDiagnosticCancellation.Current);
    }
}
