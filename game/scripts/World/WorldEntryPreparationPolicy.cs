using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>
/// Source-qualified world preparation facts. This has no access to the
/// persistent player, navigation pages, or the currently active world lease.
/// </summary>
internal static class WorldEntryPreparationPolicy
{
    internal static (long Length, long Written)? SourceFileStamp(string? path) =>
        WorldSourceFileStamp.Capture(path);

    // The engine-neutral entry planner owns loader invocation, request admission
    // and validation ordering; this Godot policy supplies only authored spawn rules.
    internal static IWorldEntrySource CreateWorldSource(Func<RuntimeWorld> load) =>
        WorldEntryPlanning.FromLoader(load, ValidateSourceEntry);

    internal static void RequireUnchangedSourceFile(string? path, (long Length, long Written)? expected) =>
        WorldSourceFileStamp.RequireUnchanged(path, expected);

    internal static RuntimeSpawn ValidateSourceEntry(RuntimeWorld world)
    {
        if (world.Game == ObpSourceGame.Rac1 && world.PlayerStart is null)
            throw new InvalidDataException("R&C1 world is missing its authored player start.");
        return WorldEntryPlanning.ResolveEntryPoint(world);
    }

    // World-local authored placement clearance, not a player capability/profile.
    internal static float SourceEntrySnapReach(RuntimeWorld world) =>
        world.Game == WorldContentIdentity.FusionLab || (world.Game == ObpSourceGame.Rac2 && world.LevelId == 0 && world.PlayerStart is not null) ? .75f : 8f;

    internal static WorldHost.Options WorldOptions(RuntimeWorld world, CommandLineArgs args)
    {
        return new WorldHost.Options
        {
            // Meshless source objects are invisible in play. Their generated
            // cubes are available only when explicitly requested for inspection.
            IncludeMobyMarkers = args.Overlay?.Split(',').Any(layer =>
                layer.Trim().Equals("isolate:moby-marker", StringComparison.OrdinalIgnoreCase)) == true,
            IncludeSky = !args.AnimSolo,
            IncludeCollision = true,
            AdmitCollisionFace = world is { Game.SourceGame: ObpSourceGame.Rac1, LevelId: 1 }
                ? Rac1NovalisWaterMechanism.AdmitCollisionFace
                : null,
            ShowCollisionDebug = args.CollisionDebug,
            OnlyAnimatedMobies = args.AnimSolo,
        };
    }
}
