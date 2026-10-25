using OBP.Runtime.Player;

namespace OBP.Fusion;

public sealed record PlayerDamageConversion(string SourceGame, string BuildId, string AdapterId, string DescriptorId);
public sealed record PlayerRewardConversion(string SourceGame, string BuildId, string AdapterId, string CurrencyId);

/// <summary>OBP choice: admitted integer source damage maps 1:1 to the selected victim's units.</summary>
public sealed record PlayerHealthPolicy(string Id, IReadOnlyList<PlayerDamageConversion> DamageConversions,
    IReadOnlyList<PlayerRewardConversion>? RewardConversions = null, int Capacity = 4, int RespawnHealth = 4,
    string HurtPolicyId = "rac1/recovered-nanotech-77/v1", string CurrencyId = "obp/bolts");

public sealed record PlayerAttackIntent(ActorInstanceKey Attacker, string SourceBuildId, string AdapterId,
    string DescriptorId, long Sequence, double NativeAmount, ulong NativeFlags);
public sealed record PlayerRewardIntent(ActorInstanceKey Actor, string SourceBuildId, long Sequence, string CurrencyId, long Amount);
public enum PlayerIntakeStatus { Applied, Protected, Duplicate, DeadPlayer, DeadActor, LiveRewardActor, StaleLease, UnregisteredActor, Unsupported, Invalid }
public sealed record PlayerIntakeResult(PlayerIntakeStatus Status, string? Reason, PlayerHealthSnapshot Health)
{
    public bool Applied => Status == PlayerIntakeStatus.Applied;
}

/// <summary>
/// One persistent victim policy. Native controllers produce/admit attack amounts;
/// actor identity, current lease and deduplication are checked before native intake.
/// </summary>
public sealed class PlayerHealthSession
{
    private readonly IPlayerHealthController _controller;
    private sealed record Actor(ActorRegistration Registration, Func<bool> Alive);
    private readonly Dictionary<ActorInstanceKey, Actor> _actors = [];
    private readonly HashSet<(ActorInstanceKey Actor, long Sequence)> _damage = [];
    private readonly HashSet<ActorInstanceKey> _rewards = [];
    private string? _world;
    private long _lease;
    public PlayerHealthPolicy Policy { get; }
    public PlayerHealthSession(PlayerHealthPolicy policy, IPlayerHealthController controller)
    {
        ArgumentNullException.ThrowIfNull(policy); ArgumentNullException.ThrowIfNull(controller);
        if (string.IsNullOrWhiteSpace(policy.Id) || string.IsNullOrWhiteSpace(policy.HurtPolicyId) ||
            string.IsNullOrWhiteSpace(policy.CurrencyId) || policy.Capacity <= 0 || policy.RespawnHealth <= 0 || policy.RespawnHealth > policy.Capacity ||
            policy.DamageConversions.Any(item => item.SourceGame is not ("rac1" or "rac2" or "rac3") ||
                string.IsNullOrWhiteSpace(item.BuildId) || string.IsNullOrWhiteSpace(item.AdapterId) || string.IsNullOrWhiteSpace(item.DescriptorId)) ||
            policy.RewardConversions?.Any(item => item.SourceGame is not ("rac1" or "rac2" or "rac3") ||
                string.IsNullOrWhiteSpace(item.BuildId) || string.IsNullOrWhiteSpace(item.AdapterId) || item.CurrencyId != policy.CurrencyId) == true)
            throw new ArgumentException("Invalid player health/conversion policy.");
        Policy = policy with
        {
            DamageConversions = Array.AsReadOnly(policy.DamageConversions.Distinct().ToArray()),
            RewardConversions = Array.AsReadOnly((policy.RewardConversions ?? []).Distinct().ToArray())
        };
        _controller = controller;
        if (Capture().Capacity != policy.Capacity) throw new ArgumentException("Selected victim controller has a different health capacity.");
    }
    public long LifeGeneration { get; private set; }
    public PlayerHealthSnapshot Capture() => _controller.Capture();
    public bool Alive => Capture().Alive;

    public void BeginWorld(string world, long lease)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(world);
        ArgumentOutOfRangeException.ThrowIfNegative(lease);
        EndWorld(); _world = world; _lease = lease;
    }

    public void EndWorld() { _actors.Clear(); _damage.Clear(); _rewards.Clear(); _world = null; }

    public void RegisterActor(ActorRegistration actor, Func<bool> alive)
    {
        actor.Validate(); ArgumentNullException.ThrowIfNull(alive);
        var frozen = actor with { Capabilities = Array.AsReadOnly(actor.Capabilities.ToArray()) };
        if (!Current(actor.Instance) || !_actors.TryAdd(actor.Instance, new(frozen, alive)))
            throw new ArgumentException("Damage actor belongs to another lease or is already registered.");
    }

    public void RetireActor(ActorInstanceKey actor)
    {
        _actors.Remove(actor);
        _damage.RemoveWhere(receipt => receipt.Actor == actor);
        _rewards.Remove(actor);
    }

    public PlayerIntakeResult Apply(PlayerAttackIntent attack)
    {
        if (!Current(attack.Attacker)) return Result(PlayerIntakeStatus.StaleLease, "Attack belongs to a retired world lease.");
        if (!_actors.TryGetValue(attack.Attacker, out var actor) || actor.Registration.Source.Build != attack.SourceBuildId)
            return Result(PlayerIntakeStatus.UnregisteredActor, "The source-qualified attacker/build is not registered.");
        if (!actor.Alive()) return Result(PlayerIntakeStatus.DeadActor, "The attacker is no longer alive.");
        if (attack.Sequence < 0 || !double.IsFinite(attack.NativeAmount) || attack.NativeAmount <= 0 ||
            attack.NativeAmount > int.MaxValue || Math.Truncate(attack.NativeAmount) != attack.NativeAmount)
            return Result(PlayerIntakeStatus.Invalid, "This policy admits positive integer source damage only.");
        if (actor.Registration.AdapterId != attack.AdapterId ||
            !actor.Registration.Capabilities.Any(capability => capability.CapabilityId == attack.DescriptorId && capability.Admission == CapabilityAdmission.Ready) ||
            !Policy.DamageConversions.Contains(new(attack.Attacker.Target.SourceGame, attack.SourceBuildId, attack.AdapterId, attack.DescriptorId)))
            return Result(PlayerIntakeStatus.Unsupported, "No admitted source attack conversion for the selected health policy.");
        if (!Alive) return Result(PlayerIntakeStatus.DeadPlayer, "The player is dead.");
        if (!_damage.Add((attack.Attacker, attack.Sequence))) return Result(PlayerIntakeStatus.Duplicate, "Attack sequence already received.");
        var before = Capture();
        try { _controller.ApplyDamageUnits((int)attack.NativeAmount); }
        catch { _damage.Remove((attack.Attacker, attack.Sequence)); throw; }
        return Result(Capture() == before ? PlayerIntakeStatus.Protected : PlayerIntakeStatus.Applied, null);
    }

    public PlayerIntakeResult Credit(PlayerRewardIntent reward, Action<long> credit)
    {
        ArgumentNullException.ThrowIfNull(credit);
        if (!Current(reward.Actor)) return Result(PlayerIntakeStatus.StaleLease, "Reward belongs to a retired lease.");
        if (!_actors.TryGetValue(reward.Actor, out var actor) || actor.Registration.Source.Build != reward.SourceBuildId)
            return Result(PlayerIntakeStatus.UnregisteredActor, "Reward source is not registered.");
        if (reward.Sequence < 0 || reward.Amount <= 0 || reward.CurrencyId != Policy.CurrencyId)
            return Result(PlayerIntakeStatus.Invalid, "Unsupported reward currency or amount.");
        if (!Policy.RewardConversions!.Contains(new(reward.Actor.Target.SourceGame, reward.SourceBuildId, actor.Registration.AdapterId, reward.CurrencyId)))
            return Result(PlayerIntakeStatus.Unsupported, "No admitted source reward conversion.");
        if (actor.Alive()) return Result(PlayerIntakeStatus.LiveRewardActor, "Actor death/collection has not been admitted.");
        if (!_rewards.Add(reward.Actor)) return Result(PlayerIntakeStatus.Duplicate, "Reward already received.");
        try { credit(reward.Amount); }
        catch { _rewards.Remove(reward.Actor); throw; }
        return Result(PlayerIntakeStatus.Applied, null);
    }

    public void TickNative() => _controller.TickNative();
    public void Heal(int amount) { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount); if (Alive) _controller.HealUnits(amount); }
    public void Respawn()
    {
        _controller.ResetForObpRespawn();
        if (Capture().Current != Policy.RespawnHealth || !Alive) throw new InvalidOperationException("Victim controller violated selected respawn policy.");
        LifeGeneration = checked(LifeGeneration + 1);
    }
    // Restoring player state does not retire the current world's attack/pickup
    // producers or rewind their receipts. The world lease owns that lifetime.
    public void RestoreGeneration(long generation) { ArgumentOutOfRangeException.ThrowIfNegative(generation); LifeGeneration = generation; }
    public void DefeatFromWorldHazard() { if (Alive) _controller.DefeatFromWorldHazard(); }
    private bool Current(ActorInstanceKey key) => _world is not null && key.WorldInstanceId == _world && key.Generation == _lease;
    private PlayerIntakeResult Result(PlayerIntakeStatus status, string? reason) => new(status, reason, Capture());
}
