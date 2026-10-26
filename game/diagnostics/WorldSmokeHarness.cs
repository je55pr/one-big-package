using System.Collections.Immutable;
using OBP.Core;
using OBP.Game.Diagnostics;

namespace OBP.Game;

/// <summary>Legacy world-smoke registration and invocation bindings; individual suites migrate in #337-#345.</summary>
internal static class WorldSmokeHarness
{
    internal sealed record Entrypoints(
        Func<Task> VeldinPlay, Func<Task> MovementContact, Func<Task> CombatContract,
        Func<Task> Campaign, Func<DiagnosticScenarioDefinition, IDiagnosticScenario> Movement,
        Func<Task>? Crates = null, Func<Task>? PlacedBolts = null, Func<Task>? Wrench = null,
        Func<Task>? Enemies = null, Func<CancellationToken, Task>? MovementState = null,
        Func<Task>? BombGlove = null, Func<DiagnosticScenarioDefinition, IDiagnosticScenario>? NovalisSwim = null, Func<Task>? NovalisPeckbot = null,
        Func<Task>? NovalisBirdbot = null, Func<Task>? NovalisMusic = null, Func<Task>? Weapons = null,
        Func<DiagnosticScenarioDefinition, IDiagnosticScenario>? PlayerAudio = null, Func<Task>? MetropolisTaxi = null, Func<Task>? MissionPlay = null,
        Func<Task>? NovalisRoute = null, Func<Task>? Frogs = null);

    private sealed record Entry(DiagnosticRegistration Registration, Func<Entrypoints, ObpDestination, DiagnosticRunContext, Task?>? Invoke,
        Func<Entrypoints, DiagnosticScenarioDefinition, IDiagnosticScenario>? Create = null);
    private static Entry Case(string id, Func<CommandLineArgs, bool> requested,
        Func<Entrypoints, ObpDestination, DiagnosticRunContext, Task?> invoke, ObpSourceGame? source = ObpSourceGame.Rac1) =>
        new(new(id, ["--" + id], requested, source), invoke);
    private static Entry TypedCase(string id, Func<CommandLineArgs, bool> requested, Func<Entrypoints, DiagnosticScenarioDefinition, IDiagnosticScenario> create, ObpSourceGame? source = ObpSourceGame.Rac1) =>
        new(new(id, ["--" + id], requested, source), null, create);

    // Original independent flag order. Only explicit first-world registrations are selected.
    private static readonly Entry[] Entries =
    [
        Case("rac1-veldin-play-smoke", a => a.Rac1VeldinPlaySmoke, (e, _, _) => e.VeldinPlay()),
        Case("rac1-movement-contact-smoke", a => a.Rac1MovementContactSmoke, (e, _, _) => e.MovementContact()),
        Case("rac1-combat-contract-smoke", a => a.Rac1CombatContractSmoke, (e, _, _) => e.CombatContract()),
        Case("rac1-wrench-smoke", a => a.Rac1WrenchSmoke, (e, _, _) => e.Wrench?.Invoke()),
        Case("rac1-bomb-glove-smoke", a => a.Rac1BombGloveSmoke, (e, _, _) => e.BombGlove?.Invoke()),
        TypedCase("rac1-novalis-swim-smoke", a => a.Rac1NovalisSwimSmoke, (e, definition) => e.NovalisSwim?.Invoke(definition)
            ?? new LegacyDiagnosticScenario(definition, _ => throw new DiagnosticUnavailableException("No swimming diagnostic is bound."))),
        Case("rac1-novalis-route-smoke", a => a.Rac1NovalisRouteSmoke, (e, _, _) => e.NovalisRoute?.Invoke()),
        Case("rac1-novalis-peckbot-smoke", a => a.Rac1NovalisPeckbotSmoke, (e, _, _) => e.NovalisPeckbot?.Invoke()),
        Case("rac1-novalis-birdbot-smoke", a => a.Rac1NovalisBirdbotSmoke, (e, _, _) => e.NovalisBirdbot?.Invoke()),
        Case("rac1-novalis-music-smoke", a => a.Rac1NovalisMusicSmoke, (e, _, _) => e.NovalisMusic?.Invoke()),
        Case("rac1-metropolis-taxi-smoke", a => a.Rac1MetropolisTaxiSmoke, (e, _, _) => e.MetropolisTaxi?.Invoke()),
        Case("rac1-weapons-smoke", a => a.Rac1WeaponsSmoke, (e, _, _) => e.Weapons?.Invoke()),
        TypedCase("rac1-player-audio-smoke", a => a.Rac1PlayerAudioSmoke, (e, definition) => e.PlayerAudio?.Invoke(definition)
            ?? new LegacyDiagnosticScenario(definition, _ => throw new DiagnosticUnavailableException("No player audio diagnostic is bound."))),
        Case("rac1-crate-smoke", a => a.Rac1CrateSmoke, (e, _, _) => e.Crates?.Invoke()),
        Case("rac1-enemy-smoke", a => a.Rac1EnemySmoke, (e, _, _) => e.Enemies?.Invoke()),
        Case("rac1-frog-smoke", a => a.Rac1FrogSmoke, (e, _, _) => e.Frogs?.Invoke()),
        Case("movement-state-smoke", a => a.MovementStateSmoke, (e, _, r) => e.MovementState?.Invoke(r.CancellationToken)),
        Case("rac1-placed-bolt-smoke", a => a.Rac1PlacedBoltSmoke, (e, _, _) => e.PlacedBolts?.Invoke()),
        Case("rac1-campaign-smoke", a => a.Rac1CampaignSmoke, (e, _, _) => e.Campaign()),
        Case("rac1-mission-play-smoke", a => a.Rac1MissionPlaySmoke, (e, _, _) => e.MissionPlay?.Invoke()),
        TypedCase("movement-smoke", a => a.MovementSmoke, (e, definition) => e.Movement(definition), source: null),
    ];
    internal static DiagnosticScenarioRegistry Registry { get; } = new(Entries.Select(entry => entry.Registration));

    // Compatibility policy used by replay admission and desktop-close classification.
    internal static bool IsRequested(CommandLineArgs args) => args.ControllerSmoke || args.ReplayForwardFrames.HasValue ||
        Registry.Registrations.Any(registration => registration.Requested(args));

    internal static IDiagnosticScenario[] Create(CommandLineArgs args, ObpDestination destination, int worldSwitches,
        Entrypoints entrypoints, bool ordinarySourceAccess, IReadOnlyList<ObpSourceDefinition> authorities)
    {
        var selected = Registry.Select(args, destination.Game, worldSwitches).Select(registration => registration.Id).ToHashSet(StringComparer.Ordinal);
        var sources = authorities.Where(authority => ordinarySourceAccess || authority.Game == destination.Game)
            .Select(authority => new DiagnosticSourceRequirement(authority.Game, authority.Authority.BuildId)).ToImmutableArray();
        return Entries.Where(entry => selected.Contains(entry.Registration.Id)).Select(entry =>
        {
            var definition = new DiagnosticScenarioDefinition(entry.Registration.Id + "/v1", ordinarySourceAccess ? DiagnosticSourceAccess.OrdinaryPlay : DiagnosticSourceAccess.ExplicitSourceDiagnostic,
                DiagnosticSetup.ExistingWorld, sources, TimeSpan.FromMinutes(30), 108_000, destination.DestinationId, FailureExitCode: 3);
            return entry.Create?.Invoke(entrypoints, definition) ?? new LegacyDiagnosticScenario(definition,
                run => entry.Invoke?.Invoke(entrypoints, destination, run) ?? throw new DiagnosticUnavailableException("No diagnostic entry point is bound: " + entry.Registration.Id));
        }).ToArray();
    }
}
