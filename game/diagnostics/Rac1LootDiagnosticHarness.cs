namespace OBP.Game.Diagnostics;

internal sealed class Rac1LootDiagnosticHarness(MovementScenarioPorts movement, Rac1LootScenarioPorts ports)
{
    internal IDiagnosticScenario CreateCrates(DiagnosticScenarioDefinition definition) => new DiagnosticProcessScenario(
        new MovementDiagnosticScenario(definition, movement, context => new Rac1CrateScenario(context, ports).RunAsync()),
        new("rac1-crate-smoke-pass", 0), new("rac1-crate-smoke-fail", 1));
    internal IDiagnosticScenario CreatePlacedBolts(DiagnosticScenarioDefinition definition) => new DiagnosticProcessScenario(
        new MovementDiagnosticScenario(definition, movement, context => new Rac1PlacedBoltScenario(context, ports).RunAsync()),
        new("rac1-placed-bolt-smoke-pass", 0), new("rac1-placed-bolt-smoke-fail", 1));
}
