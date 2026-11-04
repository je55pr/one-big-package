using System.Collections.Immutable;
using OBP.Core;

namespace OBP.Game.Diagnostics;

/// <summary>Application-lifetime menu scenarios and bounded nested pause checks. No root or mutable host state.</summary>
internal sealed class UiDiagnosticHarness(CommandLineArgs args, bool ordinarySourceAccess,
    ImmutableArray<DiagnosticSourceRequirement> authorities, IDiagnosticMenuFixture menus,
    IDiagnosticPlayerContinuity player, IDiagnosticProfileEvidence profile, IDiagnosticSourceRepairFixture repair,
    IDiagnosticRetailPauseFixture retail, IDiagnosticWorldObservations worlds, IDiagnosticNavigationActions navigation,
    DiagnosticScenarioRunner runner, HostOperationScope operations, Action<string, int> quit, Action<string> log, Action<string> logError) : IRac1EncounterMenuChecks
{
    internal IDiagnosticMenuFixture Menus => menus;
    Task IRac1EncounterMenuChecks.CheckVeldinPauseAsync(CancellationToken token) => RunVeldinPauseAsync(token);
    Task IRac1EncounterMenuChecks.CheckRetailNavigationAsync(string world, CancellationToken token) => RunRetailNavigationAsync(world, token);
    internal static DiagnosticScenarioRegistry Registry { get; } = new(
    [
        new("ordinary-ui-smoke", ["--ordinary-ui-smoke"], a => a.OrdinaryUiSmoke),
        new("source-setup-check", ["--source-setup-check"], a => a.SourceSetupCheck),
        new("rac1-startup-visibility-smoke", ["--rac1-startup-visibility-smoke"], a => a.Rac1StartupVisibilitySmoke, ObpSourceGame.Rac1),
        new("rac1-veldin-retail-pause", [], _ => false, ObpSourceGame.Rac1),
        new("rac1-world-retail-navigation", [], _ => false, ObpSourceGame.Rac1),
    ]);

    internal void Start(string id)
    {
        if (!Registry.Find(id).Requested(args)) throw new DiagnosticUnavailableException("The UI diagnostic was not explicitly requested: " + id);
        bool repairCase = id is "ordinary-ui-smoke" or "source-setup-check";
        var definition = Definition(id, ordinary: repairCase || ordinarySourceAccess);
        IDiagnosticScenario scenario = id switch
        {
            "ordinary-ui-smoke" => new OrdinaryUiScenario(definition, args.OrdinaryUiCase, menus, player, profile, repair, log),
            "source-setup-check" => new SourceSetupScenario(definition, menus, player, profile, repair, log),
            "rac1-startup-visibility-smoke" => new StartupVisibilityScenario(definition, menus, worlds, navigation, log),
            _ => throw new DiagnosticUnavailableException("This UI diagnostic is a nested live-world check: " + id),
        };
        string failureReason = id == "rac1-startup-visibility-smoke" ? id + "-failed" : id + "-fail";
        var process = new DiagnosticProcessScenario(scenario, new(id + "-complete", repairCase ? 2 : 0), new(failureReason, 1), requirePolicyExit: repairCase);
        DiagnosticScenarioDispatch.Start([process], operations, runner, quit, message => logError("[" + id + "] FAIL: " + message));
    }

    internal Task RunVeldinPauseAsync(CancellationToken cancellationToken) => RunNestedAsync(
        new Rac1VeldinRetailPauseScenario(Definition("rac1-veldin-retail-pause", ordinarySourceAccess), retail, log), cancellationToken);
    internal Task RunRetailNavigationAsync(string world, CancellationToken cancellationToken) => RunNestedAsync(
        new Rac1RetailNavigationScenario(Definition("rac1-world-retail-navigation", ordinarySourceAccess), world, retail, log), cancellationToken);
    private async Task RunNestedAsync(IDiagnosticScenario scenario, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(scenario, explicitlyRequested: true, cancellationToken);
        if (result.Status == DiagnosticRunStatus.Passed) return;
        if (result.Status == DiagnosticRunStatus.Canceled && cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(cancellationToken);
        throw new InvalidOperationException(result.Detail, result.Error);
    }
    private DiagnosticScenarioDefinition Definition(string id, bool ordinary)
    {
        Registry.Find(id);
        // Menu fixtures retain the existing application and may enter/retire a world. They do not own a world-setup lease.
        return new(id + "/v1", ordinary ? DiagnosticSourceAccess.OrdinaryPlay : DiagnosticSourceAccess.ExplicitSourceDiagnostic,
            DiagnosticSetup.AssetFreeFixture, ordinary ? authorities : [.. authorities.Where(source => source.Game == ObpSourceGame.Rac1)],
            TimeSpan.FromMinutes(2), 7200);
    }
}
