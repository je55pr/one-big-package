namespace OBP.Game.Diagnostics;

/// <summary>Registers encounter scenarios over focused source facts and scoped fixture actions.</summary>
internal sealed class Rac1EncounterDiagnosticHarness(MovementScenarioPorts movement,
    IRac1DiagnosticLootFacts loot, IRac1EnemyDiagnosticCapture capture,
    IRac1AridiaDiagnosticFixture aridia, IRac1FrogDiagnosticFixture frogs, Rac1CombatScenarioPorts combat)
{
    internal IRac1CombatDiagnosticFixture Combat => combat.Combat;
    internal IDiagnosticScenario CreateVeldin(DiagnosticScenarioDefinition definition) => new DiagnosticProcessScenario(
        new MovementDiagnosticScenario(definition, movement, context => new Rac1VeldinScenario(context, combat).RunAsync()),
        new("rac1-veldin-play-smoke-pass", 0), new("rac1-veldin-play-smoke-fail", 3));
    internal IDiagnosticScenario CreateMovementContact(DiagnosticScenarioDefinition definition) => new DiagnosticProcessScenario(
        new MovementDiagnosticScenario(definition, movement, context => new Rac1VeldinScenario(context, combat).RunMovementContactAsync()),
        new("rac1-movement-contact-smoke-pass", 0), new("rac1-movement-contact-smoke-fail", 3));
    internal IDiagnosticScenario CreateCombat(DiagnosticScenarioDefinition definition) => new DiagnosticProcessScenario(
        new MovementDiagnosticScenario(definition, movement, context => new Rac1CombatContractScenario(context, combat).RunAsync()),
        new("rac1-combat-contract-pass", 0), new("rac1-combat-contract-fail", 3));
    internal IDiagnosticScenario CreateFrogs(DiagnosticScenarioDefinition definition) => new DiagnosticProcessScenario(
        new MovementDiagnosticScenario(definition, movement, context => new Rac1FrogScenario(context, combat.Enemies, combat.Actors, frogs, combat.Lifecycle).RunAsync()),
        new("rac1-frog-smoke-pass", 0), new("rac1-frog-smoke-fail", 1));
    internal IDiagnosticScenario CreateEnemies(DiagnosticScenarioDefinition definition)
    {
        if (movement.World.Read() is { Source: OBP.Core.ObpSourceGame.Rac1, LevelId: OBP.RAC1.Gameplay.Rac1AridiaEnemies.LevelId })
            return new DiagnosticProcessScenario(new MovementDiagnosticScenario(definition, movement, context =>
                new Rac1AridiaScenario(context, combat.Enemies, combat.Actors, aridia, combat.Lifecycle).RunAsync()),
                new("rac1-aridia-constructorbot-smoke-pass", 0), new("rac1-aridia-constructorbot-smoke-fail", 4));
        return new DiagnosticProcessScenario(new MovementDiagnosticScenario(definition, movement, context =>
            new Rac1EnemyScenario(context, combat.Enemies, combat.Actors, loot, combat.Lifecycle, capture, combat.Approach, combat.Equipment).RunAsync()),
            new("rac1-enemy-smoke-pass", 0), new("rac1-enemy-smoke-fail", 1));
    }
    internal IDiagnosticScenario CreateBirdbot(DiagnosticScenarioDefinition definition) => new DiagnosticProcessScenario(
        new MovementDiagnosticScenario(definition, movement, context => new Rac1NovalisEnemyScenarios(context, combat.Enemies, combat.Actors).BirdbotAsync()),
        new("rac1-novalis-birdbot-smoke-pass", 0), new("rac1-novalis-birdbot-smoke-fail", 1));
    internal IDiagnosticScenario CreatePeckbot(DiagnosticScenarioDefinition definition) => new DiagnosticProcessScenario(
        new MovementDiagnosticScenario(definition, movement, context => new Rac1NovalisEnemyScenarios(context, combat.Enemies, combat.Actors).PeckbotAsync()),
        new("rac1-novalis-peckbot-smoke-pass", 0), new("rac1-novalis-peckbot-smoke-fail", 1));
}
