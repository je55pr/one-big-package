using OBP.Runtime;
using OBP.Runtime.Player;

namespace OBP.Fusion;

/// <summary>Selected at launch/session creation, never inferred from the world.</summary>
public enum WorldEntryMode { OrdinaryFusion, RetailParity, Diagnostic }

public sealed record WorldEntryRequest(string DestinationId, string WorldInstanceId, long Generation, WorldEntryMode Mode)
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
}

/// <summary>Source campaign/checkpoint or authored scenario supplies entry facts.</summary>
public sealed record PreparedWorldEntry(RuntimeWorld World, RuntimeSpawn EntryPoint, IReadOnlyList<ActorRegistration> Actors);

/// <summary>Native provider and custom generator implement the same staging port.</summary>
public interface IWorldEntrySource
{
    PreparedWorldEntry Prepare(WorldEntryRequest request);
}

public sealed record WorldEntryPlan(
    WorldEntryRequest Request, PreparedWorldEntry Prepared,
    IObpPlayerSession PlayerSession, PlayerSessionSnapshot Player, IPlayerAvatarProvider AvatarProvider);

/// <summary>
/// Engine-neutral preparation only. It cannot tear down a live world or choose
/// player services from world provenance. The shared Godot lifecycle consumes it.
/// </summary>
public static class WorldEntryPlanning
{
    /// <summary>Authored starts win; an unauthored ship point must be within usable world bounds.</summary>
    public static RuntimeSpawn ResolveEntryPoint(RuntimeWorld world)
    {
        if (world.PlayerStart is { } authored) return authored;
        var bounds = world.Bounds;
        if (world.Ship is { } ship && ship.X > bounds.Min.X && ship.X < bounds.Max.X &&
            ship.Z > bounds.Min.Z && ship.Z < bounds.Max.Z && ship.Y > bounds.Min.Y - 4 && ship.Y < bounds.Max.Y + 40) return ship;
        return new((bounds.Min.X + bounds.Max.X) * .5, bounds.Max.Y + 6, (bounds.Min.Z + bounds.Max.Z) * .5, 0);
    }

    public static WorldEntryPlan Prepare(
        WorldEntryRequest request, IWorldEntrySource source, IObpPlayerSession player,
        PlayerAvatarProviderRegistry avatars)
    {
        if (request.Version != WorldEntryRequest.CurrentVersion || !Enum.IsDefined(request.Mode) ||
            string.IsNullOrWhiteSpace(request.DestinationId) || string.IsNullOrWhiteSpace(request.WorldInstanceId) ||
            request.Generation < 0)
            throw new ArgumentException("Unsupported or invalid world entry request.");
        var snapshot = player.Capture();
        snapshot.Validate();
        var selected = snapshot.Configuration.Avatar;
        var provider = avatars.Get(selected.SourceGame);
        if (provider is null || provider.BuildId != selected.BuildId || !provider.CanLoad(selected.AvatarId))
            throw new InvalidOperationException($"Selected avatar {selected.SourceGame}/{selected.BuildId}/{selected.AvatarId} is unavailable.");
        var prepared = source.Prepare(request);
        var keys = new HashSet<ActorInstanceKey>();
        foreach (var actor in prepared.Actors)
        {
            actor.Validate();
            if (actor.Instance.WorldInstanceId != request.WorldInstanceId || actor.Instance.Generation != request.Generation ||
                !keys.Add(actor.Instance))
                throw new ArgumentException("Duplicate actor or actor registered to a different world lease.");
        }
        return new(request, prepared, player, snapshot, provider);
    }
}

/// <summary>
/// Single scene activation/exit implementation. Stage first;
/// commit a successful plan at one boundary. Dispose only world-local resources,
/// subscriptions and actors, never PlayerSession. Activation failure retains it.
/// </summary>
public interface IWorldEntryHost
{
    IDisposable Activate(WorldEntryPlan plan);
}
