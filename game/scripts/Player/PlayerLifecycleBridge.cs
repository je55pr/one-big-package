using OBP.Fusion;
using OBP.Runtime.Player;

namespace OBP.Game;

internal enum PlayerLifecycleEventKind { Sample, DamageSample, Hurt, Defeated, Respawned, RespawnRejected }
internal sealed record PlayerLifecycleEvent(PlayerLifecycleEventKind Kind, PlayerHealthSnapshot Before, PlayerHealthSnapshot Health);
internal sealed record PlayerLifecycleSessionPorts(Func<PlayerSessionNativeState?> Native, Func<ObpPlayerSession?> Session,
    Func<bool> ParityAlive, Func<bool> EnterWorld, Action<PlayerLifecycleEvent> Present);

/// <summary>Persistent player health orchestration; registrations belong to one disposable world lease.</summary>
internal sealed class PlayerLifecycleBridge(PlayerLifecycleSessionPorts ports)
{
    private DamageWorld? _world;
    private bool _deathPresented;
    internal long ServiceTick { get; private set; }
    internal string? WorldId => _world?.Id;
    internal long Generation => _world?.Generation ?? 0;
    internal bool Alive => ports.Native()?.Life.Alive ?? ports.ParityAlive();

    internal IDisposable BeginWorld(string id, long generation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfNegative(generation);
        _world?.Dispose();
        var life = ports.Native()?.Life;
        life?.BeginWorld(id, generation);
        _deathPresented = false;
        return _world = new(this, id, generation, life);
    }

    internal void RetireWorld() => _world?.Dispose();
    internal bool Contains(ActorInstanceKey actor) => _world?.Actors.Contains(actor) == true;
    internal void RetireActor(ActorInstanceKey actor)
    {
        _world?.Actors.Remove(actor);
        ports.Native()?.Life.RetireActor(actor);
    }

    internal PlayerIntakeResult Apply(ActorRegistration registration, PlayerAttackIntent attack, Func<bool> alive, bool belongsToScene)
    {
        var native = ports.Native() ?? throw new InvalidOperationException("Source damage requires the persistent player.");
        if (!belongsToScene || _world is not { } world || attack.Attacker.WorldInstanceId != world.Id || attack.Attacker.Generation != world.Generation)
            return new(PlayerIntakeStatus.StaleLease, "Attack producer belongs to a retired world scene.", native.Life.Capture());
        if (world.Actors.Add(attack.Attacker))
        {
            try { native.Life.RegisterActor(registration, () => ReferenceEquals(_world, world) && alive()); }
            catch { world.Actors.Remove(attack.Attacker); throw; }
        }
        var before = native.Life.Capture();
        var result = native.Life.Apply(attack);
        ports.Present(new(result.Applied ? PlayerLifecycleEventKind.Hurt : PlayerLifecycleEventKind.DamageSample, before, result.Health));
        return result;
    }

    internal void Tick(bool playerPresent, double playerHeight, double? deathHeight)
    {
        if (_world is null) return;
        ServiceTick++;
        if (!playerPresent || ports.Native() is not { } native) return;
        native.Life.TickNative();
        // Explicit OBP policy over authored geometry; no native campaign restart is inferred.
        if (deathHeight is { } threshold && playerHeight < threshold) native.Life.DefeatFromWorldHazard();
        var health = native.Life.Capture();
        ports.Present(new(PlayerLifecycleEventKind.Sample, health, health));
        if (health.Alive || _deathPresented) return;
        _deathPresented = true;
        ports.Present(new(PlayerLifecycleEventKind.Defeated, health, health));
    }

    internal bool Respawn()
    {
        if (ports.Native() is not { } native || ports.Session() is not { } session || Alive) return false;
        var before = session.Capture();
        var health = native.Life.Capture();
        // OBP entry-point policy; inventory, wallet and source campaign remain persistent.
        native.Life.Respawn();
        session.Configure(session.Capture().Configuration);
        void Reject()
        {
            session.Restore(before);
            ports.Present(new(PlayerLifecycleEventKind.RespawnRejected, health, native.Life.Capture()));
        }
        try
        {
            if (ports.EnterWorld())
            {
                _deathPresented = false;
                ports.Present(new(PlayerLifecycleEventKind.Respawned, health, native.Life.Capture()));
                return true;
            }
        }
        catch
        {
            Reject();
            throw;
        }
        Reject();
        return false;
    }

    private sealed class DamageWorld(PlayerLifecycleBridge owner, string id, long generation, PlayerHealthSession? life) : IDisposable
    {
        private bool _disposed;
        internal string Id { get; } = id;
        internal long Generation { get; } = generation;
        internal HashSet<ActorInstanceKey> Actors { get; } = [];
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Actors.Clear();
            if (!ReferenceEquals(owner._world, this)) return;
            life?.EndWorld();
            owner._world = null;
        }
    }
}
