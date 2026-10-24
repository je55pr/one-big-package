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
/// player services from world provenance. The Godot lifecycle consumes this port
/// in #267; current ordinary host paths have not migrated yet.
/// </summary>
public static class WorldEntryPlanning
{
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
/// #267 supplies the single scene activation/exit implementation. Stage first;
/// commit a successful plan at one boundary. Dispose only world-local resources,
/// subscriptions and actors, never PlayerSession. Activation failure retains it.
/// </summary>
public interface IWorldEntryHost
{
    IDisposable Activate(WorldEntryPlan plan);
}
