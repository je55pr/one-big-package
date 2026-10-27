namespace OBP.Game.Diagnostics;

internal sealed class Rac1WorldRouteDiagnosticHarness(MovementScenarioPorts movement, Func<IRac1NovalisRouteFixture> route,
    Func<IRac1NovalisMusicFixture> music, Func<IRac1TaxiDiagnosticFixture> taxi, IRac1EncounterMenuChecks menuChecks,
    IRac1WorldRouteLifetimeFixture resources, IDiagnosticMenuFixture menus, IDiagnosticNavigationActions navigation, IDiagnosticSourceAttachmentFixture sources)
{
    internal IDiagnosticScenario CreateRoute(DiagnosticScenarioDefinition definition) => Create(definition, "rac1-novalis-route-smoke", 1,
        context => new Rac1NovalisRouteScenario(context, route(), menuChecks).RunAsync());
    internal IDiagnosticScenario CreateMusic(DiagnosticScenarioDefinition definition) => Create(definition, "rac1-novalis-music-smoke", 1,
        context => new Rac1NovalisMusicScenario(context, music()).RunAsync());
    internal IDiagnosticScenario CreateTaxi(DiagnosticScenarioDefinition definition) => Create(definition, "rac1-metropolis-taxi-smoke", 3,
        context => new Rac1MetropolisTaxiScenario(context, taxi()).RunAsync());
    private IDiagnosticScenario Create(DiagnosticScenarioDefinition definition, string id, int failureExit, Func<MovementDiagnosticContext, Task> execute) =>
        new DiagnosticProcessScenario(new MovementDiagnosticScenario(definition, movement, async context =>
        {
            var entry = resources.Read();
            Rac1WorldRouteLifetimeScenario.RequireIsolated(entry);
            await execute(context);
            await new Rac1WorldRouteLifetimeScenario(resources, menus, navigation, sources, movement.Log).RunAsync(context, entry);
        }), new(id + "-pass", 0), new(id + "-fail", failureExit));
}
