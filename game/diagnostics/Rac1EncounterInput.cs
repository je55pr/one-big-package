namespace OBP.Game.Diagnostics;

/// <summary>Original two-tick logical attack edges, owned by the run's input lease.</summary>
public static class Rac1EncounterInput
{
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
