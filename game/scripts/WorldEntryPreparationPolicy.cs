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
    internal static (long Length, long Written)? SourceFileStamp(string? path)
    {
        if (path is null) return null;
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("The world source is missing. Reattach it in Game Sources.");
        return (file.Length, file.LastWriteTimeUtc.Ticks);
    }

    // Source preparation owns the native world loading adapter, not the Godot
    // composition root. Validation still runs before a prepared plan is staged.
    private sealed class PlannedWorldSource(Func<RuntimeWorld> load) : IWorldEntrySource
    {
        public PreparedWorldEntry Prepare(WorldEntryRequest _)
        {
            var world = load();
            var spawn = ValidateSourceEntry(world);
            return new(world, spawn, []); // Mixed native actor adapters are registered by #256.
        }
    }

    internal static IWorldEntrySource CreateWorldSource(Func<RuntimeWorld> load) => new PlannedWorldSource(load);

    internal static void RequireUnchangedSourceFile(string? path, (long Length, long Written)? expected)
    {
        if (SourceFileStamp(path) != expected)
            throw new InvalidDataException("The world source changed during preparation. Reattach it in Game Sources.");
    }

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
        bool framedCapture = args.CaptureFrame is not null
            && (args.DirectLoad || args.Destination is not null)
            && args.TestScene != "player"
            && !args.AnimSolo
            && !args.CrateFocus && !args.CrateAutoStrike;
        return new WorldHost.Options
        {
            IncludeMobyMarkers = !(args.CaptureFrame is not null && !framedCapture),
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
