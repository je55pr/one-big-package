using System.Numerics;

namespace OBP.Game.Diagnostics;

/// <summary>Original two-tick logical attack edges, owned by the run's input lease.</summary>
public static class Rac1EncounterInput
{
    public static (double Right, double Forward) ConditionedAxesToward(Vector3 desiredPlanarUnit, double controlYaw, float inputScale)
    {
        if (!(inputScale > 0 && inputScale <= 1)) throw new ArgumentOutOfRangeException(nameof(inputScale));
        Vector3 forward = new(-(float)Math.Cos(controlYaw), 0, (float)Math.Sin(controlYaw));
        Vector3 right = new(-(float)Math.Sin(controlYaw), 0, -(float)Math.Cos(controlYaw));
        return (inputScale * Math.Clamp(Vector3.Dot(desiredPlanarUnit, right), -1, 1),
            inputScale * Math.Clamp(Vector3.Dot(desiredPlanarUnit, forward), -1, 1));
    }
    public static async Task TapPrimaryAsync(MovementDiagnosticContext context, IDiagnosticControllerEquipment equipment)
    {
        string action = equipment.Read().Selected == OBP.RAC1.Gameplay.Rac1WeaponId.FirstRanged
            ? MovementDiagnosticDriver.Traversal : MovementDiagnosticDriver.Action;
        await TapAsync(context, action);
    }
    public static Task TapWrenchAsync(MovementDiagnosticContext context) => TapAsync(context, MovementDiagnosticDriver.Action);
    private static async Task TapAsync(MovementDiagnosticContext context, string action)
    {
        context.Driver.SetAction(action, true);
        await context.Driver.PhysicsAsync(2);
        context.Driver.SetAction(action, false);
        await context.Driver.PhysicsAsync(2);
    }
}
