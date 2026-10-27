namespace OBP.Game.Diagnostics;

/// <summary>Composes equipment acceptance over scoped input and narrow observations/actions.</summary>
internal sealed class WeaponDiagnosticHarness(MovementScenarioPorts movement, IDiagnosticControllerEquipment equipment,
    IRac1WrenchDiagnosticFacts wrench, IRac1WeaponTargetFixture targets, IRac1CombatDiagnosticFixture combat, IWeaponDiagnosticCapture capture,
    IRac1BombAcceptanceFixture bomb, IWeaponMovementStateFixture reproduction)
{
    internal Task CaptureBombAsync(string name, System.Numerics.Vector3 focus, bool close, CancellationToken token) =>
        capture.CaptureAsync(WeaponDiagnosticCaptureKind.Bomb, name, focus, close, token);
    internal IDiagnosticScenario CreateBomb(DiagnosticScenarioDefinition definition) => new DiagnosticProcessScenario(
        new MovementDiagnosticScenario(definition, movement, context => new Rac1BombGloveScenario(context, bomb, targets, combat, equipment, capture, reproduction).RunAsync()),
        new(bomb.SavedReproductionPath is null ? "rac1-bomb-glove-smoke-pass" : "rac1-bomb-glove-repro-pass", 0), new("rac1-bomb-glove-smoke-fail", 1));
    internal IDiagnosticScenario CreateWrench(DiagnosticScenarioDefinition definition) => new DiagnosticProcessScenario(
        new MovementDiagnosticScenario(definition, movement, context => new Rac1WrenchScenario(context, wrench, targets, combat, equipment, capture).RunAsync()),
        new("rac1-wrench-smoke-pass", 0), new("rac1-wrench-smoke-fail", 1));
}
