using OBP.Core;

namespace OBP.Game;

/// <summary>
/// Diagnostic-only dispatcher: ordinary destination loading has one callback,
/// while each opt-in smoke gate retains its own proven source-game restriction.
/// The legacy order and independent flags are intentional for CLI compatibility.
/// </summary>
internal static class WorldSmokeHarness
{
    internal sealed record Entrypoints(
        Func<Task> VeldinPlay,
        Func<Task> MovementContact,
        Func<Task> CombatContract,
        Func<Task> Campaign,
        Func<ObpDestination, Task> Movement,
        Func<Task>? Crates = null,
        Func<Task>? PlacedBolts = null,
        Func<Task>? Wrench = null,
        Func<Task>? Enemies = null,
        Func<Task>? MovementState = null,
        Func<Task>? BombGlove = null,
        Func<Task>? NovalisSwim = null,
        Func<Task>? NovalisPeckbot = null,
        Func<Task>? NovalisBirdbot = null,
        Func<Task>? NovalisMusic = null,
        Func<Task>? Weapons = null,
        Func<Task>? PlayerAudio = null,
        Func<Task>? MetropolisTaxi = null,
        Func<Task>? MissionPlay = null,
        Func<Task>? NovalisRoute = null,
        Func<Task>? Frogs = null);

    internal static bool IsRequested(CommandLineArgs args) =>
        args.ControllerSmoke || args.Rac1PlayerAudioSmoke || args.MovementStateSmoke || args.ReplayForwardFrames.HasValue ||
        args.Rac1MetropolisTaxiSmoke || args.Rac1VeldinPlaySmoke ||
        args.Rac1MovementContactSmoke ||
        args.Rac1CombatContractSmoke ||
        args.Rac1WrenchSmoke ||
        args.Rac1BombGloveSmoke ||
        args.Rac1NovalisSwimSmoke ||
        args.Rac1NovalisRouteSmoke ||
        args.Rac1NovalisPeckbotSmoke ||
        args.Rac1NovalisBirdbotSmoke ||
        args.Rac1NovalisMusicSmoke ||
        args.Rac1WeaponsSmoke ||
        args.Rac1CrateSmoke ||
        args.Rac1EnemySmoke || args.Rac1FrogSmoke ||
        args.Rac1PlacedBoltSmoke ||
        args.Rac1CampaignSmoke || args.Rac1MissionPlaySmoke ||
        args.MovementSmoke;

    internal static void Dispatch(
        CommandLineArgs args,
        ObpDestination destination,
        int worldSwitches,
        Entrypoints entrypoints)
    {
        // Never run automated tests on ordinary subsequent world transitions.
        if (worldSwitches != 1)
            return;

        if (destination.Game == ObpSourceGame.Rac1)
        {
            if (args.Rac1VeldinPlaySmoke)
                _ = entrypoints.VeldinPlay();
            if (args.Rac1MovementContactSmoke)
                _ = entrypoints.MovementContact();
            if (args.Rac1CombatContractSmoke)
                _ = entrypoints.CombatContract();
            if (args.Rac1WrenchSmoke)
                _ = entrypoints.Wrench?.Invoke();
            if (args.Rac1BombGloveSmoke)
                _ = entrypoints.BombGlove?.Invoke();
            if (args.Rac1NovalisSwimSmoke)
                _ = entrypoints.NovalisSwim?.Invoke();
            if (args.Rac1NovalisRouteSmoke)
                _ = entrypoints.NovalisRoute?.Invoke();
            if (args.Rac1NovalisPeckbotSmoke)
                _ = entrypoints.NovalisPeckbot?.Invoke();
            if (args.Rac1NovalisBirdbotSmoke)
                _ = entrypoints.NovalisBirdbot?.Invoke();
            if (args.Rac1NovalisMusicSmoke)
                _ = entrypoints.NovalisMusic?.Invoke();
            if (args.Rac1MetropolisTaxiSmoke)
                _ = entrypoints.MetropolisTaxi?.Invoke();
            if (args.Rac1WeaponsSmoke)
                _ = entrypoints.Weapons?.Invoke();
            if (args.Rac1PlayerAudioSmoke)
                _ = entrypoints.PlayerAudio?.Invoke();
            if (args.Rac1CrateSmoke)
                _ = entrypoints.Crates?.Invoke();
            if (args.Rac1EnemySmoke)
                _ = entrypoints.Enemies?.Invoke();
            if (args.Rac1FrogSmoke)
                _ = entrypoints.Frogs?.Invoke();
            if (args.MovementStateSmoke)
                _ = entrypoints.MovementState?.Invoke();
            if (args.Rac1PlacedBoltSmoke)
                _ = entrypoints.PlacedBolts?.Invoke();
            if (args.Rac1CampaignSmoke)
                _ = entrypoints.Campaign();
            if (args.Rac1MissionPlaySmoke)
                _ = entrypoints.MissionPlay?.Invoke();
        }

        if (args.MovementSmoke)
            _ = entrypoints.Movement(destination);
    }
}

/// <summary>
/// The host offers callbacks into its retained authored gameplay/test fixtures;
/// the normal world loader does not choose or invoke individual smoke tests.
/// </summary>
public partial class OBPGame
{
    private void DispatchRequestedWorldSmokes(ObpDestination destination)
    {
        if (_worldSwitches != 1 || !WorldSmokeHarness.IsRequested(_args))
            return;

        WorldSmokeHarness.Dispatch(
            _args,
            destination,
            _worldSwitches,
            new WorldSmokeHarness.Entrypoints(
                RunRac1VeldinPlaySmokeAsync,
                RunRac1MovementContactSmokeAsync,
                RunRac1CombatContractSmokeAsync,
                RunRac1CampaignSmokeAsync,
                RunMovementSmokeAsync,
                RunRac1CrateSmokeAsync,
                RunRac1PlacedBoltSmokeAsync,
                RunRac1WrenchSmokeAsync,
                RunRac1EnemySmokeAsync,
                RunMovementStateSmokeAsync,
                RunRac1BombGloveSmokeAsync,
                RunRac1NovalisSwimSmokeAsync,
                RunRac1NovalisPeckbotSmokeAsync,
                RunRac1NovalisBirdbotSmokeAsync,
                RunRac1NovalisMusicSmokeAsync,
                RunRac1WeaponsSmokeAsync,
                RunRac1PlayerAudioSmokeAsync,
                RunRac1MetropolisTaxiSmokeAsync,
                RunRac1MissionPlaySmokeAsync,
                RunRac1NovalisRouteSmokeAsync,
                RunRac1FrogSmokeAsync));
    }
}
