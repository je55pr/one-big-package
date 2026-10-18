using OBP.Core;

namespace OneBigPackage;

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
        Func<Task>? Wrench = null);

    internal static bool IsRequested(CommandLineArgs args) =>
        args.Rac1VeldinPlaySmoke ||
        args.Rac1MovementContactSmoke ||
        args.Rac1CombatContractSmoke ||
        args.Rac1WrenchSmoke ||
        args.Rac1CrateSmoke ||
        args.Rac1PlacedBoltSmoke ||
        args.Rac1CampaignSmoke ||
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
            if (args.Rac1CrateSmoke)
                _ = entrypoints.Crates?.Invoke();
            if (args.Rac1PlacedBoltSmoke)
                _ = entrypoints.PlacedBolts?.Invoke();
            if (args.Rac1CampaignSmoke)
                _ = entrypoints.Campaign();
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
                RunRac1WrenchSmokeAsync));
    }
}
