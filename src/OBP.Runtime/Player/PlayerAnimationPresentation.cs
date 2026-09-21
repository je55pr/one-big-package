namespace OBP.Runtime.Player;

/// <summary>
/// Engine-neutral presentation decision for one player-avatar clip.
/// Source-specific sequence/state identifiers are opaque diagnostics; renderers
/// consume only the resolved clip id, clock origin, and loop policy.
/// </summary>
public sealed record PlayerAnimationPresentation(
    PlayerAnimationState SemanticState,
    string ClipId,
    double ClipStartedAtSeconds,
    bool Loop,
    string? SourceSequenceKey = null,
    string? SourceStateKey = null)
{
    public double ElapsedAt(double clockSeconds)
    {
        if (!double.IsFinite(clockSeconds))
            throw new ArgumentOutOfRangeException(nameof(clockSeconds));
        return Math.Max(0, clockSeconds - ClipStartedAtSeconds);
    }
}

/// <summary>
/// Source-aware, engine-neutral owner of player animation selection semantics.
/// Controllers may interpret generic host states using recovered native rules.
/// </summary>
public interface IPlayerAnimationPresentationController
{
    PlayerAnimationPresentation Current { get; }

    PlayerAnimationPresentation SetAnimationState(PlayerAnimationState state);

    PlayerAnimationPresentation SetClock(double clockSeconds);
}

/// <summary>
/// Optional capability exposed by avatar providers that have recovered native
/// player animation-selection semantics. Providers without it use the explicit
/// generic fallback at composition time.
/// </summary>
public interface IPlayerAnimationControllerProvider
{
    IPlayerAnimationPresentationController CreateAnimationController(PlayerAvatar avatar);
}

/// <summary>Presentation-side sink for an already resolved neutral decision.</summary>
public interface IPlayerAnimationPresentationSink
{
    PlayerAnimationPresentation CurrentAnimationPresentation { get; }

    void SetAnimationPresentation(PlayerAnimationPresentation presentation);
}
