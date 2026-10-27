namespace OBP.Game.Diagnostics;

/// <summary>Composes equipment acceptance over scoped input and narrow observations/actions.</summary>
internal sealed class WeaponDiagnosticHarness(MovementScenarioPorts movement, IRac1WrenchDiagnosticFacts wrench,
    Rac1WeaponScenarioPorts ports, IWeaponDiagnosticTransientFixture transients)
{
    internal MovementScenarioPorts Movement => movement;
    internal IWeaponDiagnosticTransientFixture Transients => transients;
    internal Rac1WeaponScenarioPorts Ports => ports;
    internal IDiagnosticScenario CreateWeapons(DiagnosticScenarioDefinition definition) => new DiagnosticProcessScenario(
        new WeaponDiagnosticScenario(new MovementDiagnosticScenario(definition, movement, context => new Rac1WeaponsScenario(context, ports).RunAsync()), transients),
        new("rac1-weapons-smoke-pass", 0), new("rac1-weapons-smoke-fail", 2));
    internal IDiagnosticScenario CreateBomb(DiagnosticScenarioDefinition definition) => new DiagnosticProcessScenario(
        new WeaponDiagnosticScenario(new MovementDiagnosticScenario(definition, movement, context => new Rac1BombGloveScenario(context,
            ports.Bomb, ports.Targets, ports.Combat, ports.Equipment, ports.Capture, ports.Reproduction).RunAsync()), transients),
        new(ports.Bomb.SavedReproductionPath is null ? "rac1-bomb-glove-smoke-pass" : "rac1-bomb-glove-repro-pass", 0), new("rac1-bomb-glove-smoke-fail", 1));
    internal IDiagnosticScenario CreateWrench(DiagnosticScenarioDefinition definition) => new DiagnosticProcessScenario(
        new WeaponDiagnosticScenario(new MovementDiagnosticScenario(definition, movement, context => new Rac1WrenchScenario(context,
            wrench, ports.Targets, ports.Combat, ports.Equipment, ports.Capture).RunAsync()), transients),
        new("rac1-wrench-smoke-pass", 0), new("rac1-wrench-smoke-fail", 1));
}
