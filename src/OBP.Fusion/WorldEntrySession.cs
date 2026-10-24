using OBP.Runtime.Player;

namespace OBP.Fusion;

/// <summary>
/// A staged transition has no permission to destroy the current lease. The host
/// stages all required resources before Commit. Only lease resources are retired.
/// </summary>
public sealed class WorldEntrySession(IObpPlayerSession player)
{
    private IDisposable? _lease;
    private long _nextGeneration;
    private long _lastCommittedGeneration;
    public IObpPlayerSession Player { get; } = player;
    public WorldEntryPlan? Current { get; private set; }

    public WorldEntryRequest Request(string destination, WorldEntryMode mode) =>
        new(destination, "obp-world", checked(++_nextGeneration), mode);

    public WorldEntryPlan Prepare(WorldEntryRequest request, IWorldEntrySource source, PlayerAvatarProviderRegistry avatars) =>
        WorldEntryPlanning.Prepare(request, source, Player, avatars);

    public void Commit(WorldEntryPlan plan, IWorldEntryHost host)
    {
        if (!ReferenceEquals(plan.PlayerSession, Player) || plan.Player.Generation != Player.Capture().Generation ||
            plan.Request.Generation <= _lastCommittedGeneration || plan.Request.Generation > _nextGeneration)
            throw new InvalidOperationException("World entry belongs to a stale/different player session.");
        // All fallible source/asset/scene preparation precedes this boundary.
        // A rejection before activation leaves the old lease intact. Hosts own
        // cleanup after a destructive activation failure; Player is never disposed.
        var next = host.Activate(plan);
        var previous = _lease;
        _lease = next;
        _lastCommittedGeneration = plan.Request.Generation;
        Current = plan;
        previous?.Dispose();
        if (previous is not null && Player is ObpPlayerSession session) session.EndWorld();
    }

    public void Unload()
    {
        var previous = _lease;
        _lease = null; Current = null;
        previous?.Dispose();
        if (previous is not null && Player is ObpPlayerSession session) session.EndWorld();
    }
}
