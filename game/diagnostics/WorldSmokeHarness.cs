using System.Collections.Immutable;
using OBP.Core;
using OBP.Game.Diagnostics;

namespace OBP.Game;

/// <summary>Legacy world-smoke registration and invocation bindings; individual suites migrate in #337-#345.</summary>
internal static class WorldSmokeHarness
{
    internal sealed record Entrypoints(
        Func<DiagnosticScenarioDefinition, IDiagnosticScenario> VeldinPlay,
        Func<DiagnosticScenarioDefinition, IDiagnosticScenario> MovementContact,
        Func<DiagnosticScenarioDefinition, IDiagnosticScenario> CombatContract,
        Func<DiagnosticScenarioDefinition, IDiagnosticScenario> Campaign, Func<DiagnosticScenarioDefinition, IDiagnosticScenario> Movement,
        Func<DiagnosticScenarioDefinition, IDiagnosticScenario>? Crates = null, Func<DiagnosticScenarioDefinition, IDiagnosticScenario>? PlacedBolts = null, Func<DiagnosticScenarioDefinition, IDiagnosticScenario>? Wrench = null,
        Func<DiagnosticScenarioDefinition, IDiagnosticScenario>? Enemies = null, Func<DiagnosticScenarioDefinition, IDiagnosticScenario>? MovementState = null,
        Func<DiagnosticScenarioDefinition, IDiagnosticScenario>? BombGlove = null, Func<DiagnosticScenarioDefinition, IDiagnosticScenario>? NovalisSwim = null,
        Func<DiagnosticScenarioDefinition, IDiagnosticScenario>? NovalisPeckbot = null,
        Func<DiagnosticScenarioDefinition, IDiagnosticScenario>? NovalisBirdbot = null, Func<DiagnosticScenarioDefinition, IDiagnosticScenario>? NovalisMusic = null, Func<DiagnosticScenarioDefinition, IDiagnosticScenario>? Weapons = null,
        Func<DiagnosticScenarioDefinition, IDiagnosticScenario>? PlayerAudio = null, Func<DiagnosticScenarioDefinition, IDiagnosticScenario>? MetropolisTaxi = null, Func<DiagnosticScenarioDefinition, IDiagnosticScenario>? MissionPlay = null,
        Func<DiagnosticScenarioDefinition, IDiagnosticScenario>? NovalisRoute = null, Func<DiagnosticScenarioDefinition, IDiagnosticScenario>? Frogs = null);

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
        TypedCase("rac1-veldin-play-smoke", a => a.Rac1VeldinPlaySmoke, (e, definition) => e.VeldinPlay(definition)),
        TypedCase("rac1-movement-contact-smoke", a => a.Rac1MovementContactSmoke, (e, definition) => e.MovementContact(definition)),
        TypedCase("rac1-combat-contract-smoke", a => a.Rac1CombatContractSmoke, (e, definition) => e.CombatContract(definition)),
        TypedCase("rac1-wrench-smoke", a => a.Rac1WrenchSmoke, (e, definition) => e.Wrench?.Invoke(definition)
            ?? new LegacyDiagnosticScenario(definition, _ => throw new DiagnosticUnavailableException("No wrench diagnostic is bound."))),
        TypedCase("rac1-bomb-glove-smoke", a => a.Rac1BombGloveSmoke, (e, definition) => e.BombGlove?.Invoke(definition)
            ?? new LegacyDiagnosticScenario(definition, _ => throw new DiagnosticUnavailableException("No Bomb Glove diagnostic is bound."))),
        TypedCase("rac1-novalis-swim-smoke", a => a.Rac1NovalisSwimSmoke, (e, definition) => e.NovalisSwim?.Invoke(definition)
            ?? new LegacyDiagnosticScenario(definition, _ => throw new DiagnosticUnavailableException("No swimming diagnostic is bound."))),
        TypedCase("rac1-novalis-route-smoke", a => a.Rac1NovalisRouteSmoke, (e, definition) => e.NovalisRoute?.Invoke(definition)
            ?? new LegacyDiagnosticScenario(definition, _ => throw new DiagnosticUnavailableException("No Novalis route diagnostic is bound."))),
        TypedCase("rac1-novalis-peckbot-smoke", a => a.Rac1NovalisPeckbotSmoke, (e, definition) => e.NovalisPeckbot?.Invoke(definition)
            ?? new LegacyDiagnosticScenario(definition, _ => throw new DiagnosticUnavailableException("No Peckbot diagnostic is bound."))),
        TypedCase("rac1-novalis-birdbot-smoke", a => a.Rac1NovalisBirdbotSmoke, (e, definition) => e.NovalisBirdbot?.Invoke(definition)
            ?? new LegacyDiagnosticScenario(definition, _ => throw new DiagnosticUnavailableException("No Birdbot diagnostic is bound."))),
        TypedCase("rac1-novalis-music-smoke", a => a.Rac1NovalisMusicSmoke, (e, definition) => e.NovalisMusic?.Invoke(definition)
            ?? new LegacyDiagnosticScenario(definition, _ => throw new DiagnosticUnavailableException("No Novalis music diagnostic is bound."))),
        TypedCase("rac1-metropolis-taxi-smoke", a => a.Rac1MetropolisTaxiSmoke, (e, definition) => e.MetropolisTaxi?.Invoke(definition)
            ?? new LegacyDiagnosticScenario(definition, _ => throw new DiagnosticUnavailableException("No Metropolis taxi diagnostic is bound."))),
        TypedCase("rac1-weapons-smoke", a => a.Rac1WeaponsSmoke, (e, definition) => e.Weapons?.Invoke(definition)
            ?? new LegacyDiagnosticScenario(definition, _ => throw new DiagnosticUnavailableException("No weapon diagnostic is bound."))),
        TypedCase("rac1-player-audio-smoke", a => a.Rac1PlayerAudioSmoke, (e, definition) => e.PlayerAudio?.Invoke(definition)
            ?? new LegacyDiagnosticScenario(definition, _ => throw new DiagnosticUnavailableException("No player audio diagnostic is bound."))),
        TypedCase("rac1-crate-smoke", a => a.Rac1CrateSmoke, (e, definition) => e.Crates?.Invoke(definition)
            ?? new LegacyDiagnosticScenario(definition, _ => throw new DiagnosticUnavailableException("No crate diagnostic is bound."))),
        TypedCase("rac1-enemy-smoke", a => a.Rac1EnemySmoke, (e, definition) => e.Enemies?.Invoke(definition)
            ?? new LegacyDiagnosticScenario(definition, _ => throw new DiagnosticUnavailableException("No enemy diagnostic is bound."))),
        TypedCase("rac1-frog-smoke", a => a.Rac1FrogSmoke, (e, definition) => e.Frogs?.Invoke(definition)
            ?? new LegacyDiagnosticScenario(definition, _ => throw new DiagnosticUnavailableException("No Frog diagnostic is bound."))),
        TypedCase("movement-state-smoke", a => a.MovementStateSmoke, (e, definition) => e.MovementState?.Invoke(definition)
            ?? new LegacyDiagnosticScenario(definition, _ => throw new DiagnosticUnavailableException("No movement-state diagnostic is bound."))),
        TypedCase("rac1-placed-bolt-smoke", a => a.Rac1PlacedBoltSmoke, (e, definition) => e.PlacedBolts?.Invoke(definition)
            ?? new LegacyDiagnosticScenario(definition, _ => throw new DiagnosticUnavailableException("No placed-bolt diagnostic is bound."))),
        TypedCase("rac1-campaign-smoke", a => a.Rac1CampaignSmoke, (e, definition) => e.Campaign(definition)),
        TypedCase("rac1-mission-play-smoke", a => a.Rac1MissionPlaySmoke, (e, definition) => e.MissionPlay?.Invoke(definition)
            ?? new LegacyDiagnosticScenario(definition, _ => throw new DiagnosticUnavailableException("No mission-play diagnostic is bound."))),
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
