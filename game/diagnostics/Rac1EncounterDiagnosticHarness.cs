namespace OBP.Game.Diagnostics;

/// <summary>Registers encounter scenarios over focused source facts and scoped fixture actions.</summary>
internal sealed class Rac1EncounterDiagnosticHarness(MovementScenarioPorts movement,
    IRac1EnemyDiagnosticFacts enemies, IRac1EnemyDiagnosticActions actors, IRac1DiagnosticLootFacts loot,
    IRac1EncounterLifecycle lifecycle, IRac1EnemyDiagnosticCapture capture, IRac1WrenchApproachFixture approach,
    IDiagnosticControllerEquipment equipment, IRac1AridiaDiagnosticFixture aridia)
{
    internal IDiagnosticScenario CreateEnemies(DiagnosticScenarioDefinition definition)
    {
        if (movement.World.Read() is { Source: OBP.Core.ObpSourceGame.Rac1, LevelId: OBP.RAC1.Gameplay.Rac1AridiaEnemies.LevelId })
            return new DiagnosticProcessScenario(new MovementDiagnosticScenario(definition, movement, context =>
                new Rac1AridiaScenario(context, enemies, actors, aridia, lifecycle).RunAsync()),
                new("rac1-aridia-constructorbot-smoke-pass", 0), new("rac1-aridia-constructorbot-smoke-fail", 4));
        return new DiagnosticProcessScenario(new MovementDiagnosticScenario(definition, movement, context =>
            new Rac1EnemyScenario(context, enemies, actors, loot, lifecycle, capture, approach, equipment).RunAsync()),
            new("rac1-enemy-smoke-pass", 0), new("rac1-enemy-smoke-fail", 1));
    }
    internal IDiagnosticScenario CreateBirdbot(DiagnosticScenarioDefinition definition) => new DiagnosticProcessScenario(
        new MovementDiagnosticScenario(definition, movement, context => new Rac1NovalisEnemyScenarios(context, enemies, actors).BirdbotAsync()),
        new("rac1-novalis-birdbot-smoke-pass", 0), new("rac1-novalis-birdbot-smoke-fail", 1));
    internal IDiagnosticScenario CreatePeckbot(DiagnosticScenarioDefinition definition) => new DiagnosticProcessScenario(
        new MovementDiagnosticScenario(definition, movement, context => new Rac1NovalisEnemyScenarios(context, enemies, actors).PeckbotAsync()),
        new("rac1-novalis-peckbot-smoke-pass", 0), new("rac1-novalis-peckbot-smoke-fail", 1));
}
