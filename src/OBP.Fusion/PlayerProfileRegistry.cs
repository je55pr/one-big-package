using OBP.Runtime.Player;

namespace OBP.Fusion;

/// <summary>Admission of real composition-root registrations, independently of world content.</summary>
public sealed class PlayerProfileRegistry
{
    private readonly Dictionary<SourceProfileKey, PlayerControlProfile> _movement;
    private readonly Dictionary<SourceProfileKey, PlayerAnimationProfile> _animation;
    private readonly HashSet<PlayerServiceBindings> _services;

    public PlayerProfileRegistry(IEnumerable<PlayerControlProfile> movement, IEnumerable<PlayerAnimationProfile> animation,
        IEnumerable<PlayerServiceBindings> services)
    {
        _movement = movement.ToDictionary(item => item.Identity);
        _animation = animation.ToDictionary(item => item.Identity, item => item with { CompatibleAvatars = Array.AsReadOnly(item.CompatibleAvatars.ToArray()) });
        _services = services.ToHashSet();
        foreach (var profile in _movement.Values) profile.Validate();
        foreach (var profile in _animation.Values)
        {
            profile.Identity.Validate();
            if (string.IsNullOrWhiteSpace(profile.AdapterId) || profile.NativeTicksPerSecond <= 0) throw new ArgumentException("Invalid animation adapter.");
        }
    }

    public (PlayerControlProfile Control, PlayerAnimationProfile Animation) Resolve(PlayerSessionConfiguration selected)
    {
        selected.Validate();
        if (!_services.Contains(selected.Services)) throw new InvalidOperationException("The selected player service bindings have no admitted composition.");
        if (!_movement.TryGetValue(selected.Movement, out var movement)) throw new InvalidOperationException($"Movement profile {selected.Movement.ProfileId} is unavailable.");
        if (!_animation.TryGetValue(selected.Animation, out var animation) || animation.NativeTicksPerSecond != selected.NativeTicksPerSecond)
            throw new InvalidOperationException("The selected animation profile/clock has no admitted adapter.");
        if (!animation.CompatibleAvatars.Contains(selected.Avatar))
            throw new InvalidOperationException("The selected avatar has no measured compatibility admission for this animation profile.");
        return (movement, animation);
    }
}

/// <summary>One fixed-rate player clock. Rendering observes it and cannot advance it.</summary>
public sealed class PlayerNativeClock(int ticksPerSecond)
{
    public int TicksPerSecond { get; } = ticksPerSecond > 0 ? ticksPerSecond : throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
    public long Tick { get; private set; }
    public double Seconds => (double)Tick / TicksPerSecond;
    public double Step() { Tick = checked(Tick + 1); return Seconds; }
    public void Reset() => Tick = 0;
}
