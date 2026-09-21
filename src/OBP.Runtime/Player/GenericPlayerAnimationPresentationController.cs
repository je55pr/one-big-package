namespace OBP.Runtime.Player;

/// <summary>
/// Explicit fallback for avatars whose source provider has not recovered native
/// animation selection. This preserves the old semantic role mapping without
/// making it presentation-engine policy.
/// </summary>
public sealed class GenericPlayerAnimationPresentationController : IPlayerAnimationPresentationController
{
    private readonly PlayerAvatar _avatar;
    private double _clockSeconds;
    private PlayerAnimationState _airborneReturnState = PlayerAnimationState.Idle;
    private PlayerAnimationState _attackReturnState = PlayerAnimationState.Idle;

    public GenericPlayerAnimationPresentationController(PlayerAvatar avatar)
    {
        _avatar = avatar;
        Current = Presentation(PlayerAnimationState.Idle, PlayerAvatarAnimationRole.Standing, loop: true, 0);
    }

    public PlayerAnimationPresentation Current { get; private set; }

    public PlayerAnimationPresentation SetClock(double clockSeconds)
    {
        if (!double.IsFinite(clockSeconds))
            throw new ArgumentOutOfRangeException(nameof(clockSeconds));
        _clockSeconds = Math.Max(0, clockSeconds);
        CompleteLocomotionStartIfNeeded();
        CompleteAttackIfNeeded();
        return Current;
    }

    public PlayerAnimationPresentation SetAnimationState(PlayerAnimationState state)
    {
        if (state == Current.SemanticState)
            return Current;

        if (Current.SemanticState == PlayerAnimationState.Attack)
        {
            _attackReturnState = GroundContext(state);
            return Current;
        }

        if (CurrentClip.Role == PlayerAvatarAnimationRole.LocomotionStart && IsLocomotion(state))
        {
            Current = Current with { SemanticState = state };
            _airborneReturnState = state;
            return Current;
        }

        PlayerAnimationState previous = Current.SemanticState;
        switch (state)
        {
            case PlayerAnimationState.Idle:
                _airborneReturnState = PlayerAnimationState.Idle;
                Select(state, PlayerAvatarAnimationRole.Standing, loop: true,
                    restart: CurrentClip.Role != PlayerAvatarAnimationRole.Standing);
                break;

            case PlayerAnimationState.Walk:
            case PlayerAnimationState.Run:
                _airborneReturnState = state;
                bool startFromIdle = previous == PlayerAnimationState.Idle &&
                                     CurrentClip.Role == PlayerAvatarAnimationRole.Standing;
                Select(state,
                    startFromIdle ? PlayerAvatarAnimationRole.LocomotionStart : PlayerAvatarAnimationRole.SustainedLocomotion,
                    loop: !startFromIdle,
                    restart: startFromIdle || CurrentClip.Role != PlayerAvatarAnimationRole.SustainedLocomotion);
                break;

            case PlayerAnimationState.JumpRise:
                bool moving = IsLocomotion(previous) ||
                              CurrentClip.Role == PlayerAvatarAnimationRole.SustainedLocomotion;
                _airborneReturnState = moving ? LocomotionContext(previous) : PlayerAnimationState.Idle;
                Select(state,
                    moving ? PlayerAvatarAnimationRole.MovingJump : PlayerAvatarAnimationRole.StationaryJump,
                    loop: false, restart: true);
                break;

            case PlayerAnimationState.Fall:
                if (CurrentClip.Role is PlayerAvatarAnimationRole.StationaryJump or PlayerAvatarAnimationRole.MovingJump)
                {
                    Current = Current with { SemanticState = state };
                }
                else
                {
                    bool movingFall = IsLocomotion(previous) ||
                                      CurrentClip.Role == PlayerAvatarAnimationRole.SustainedLocomotion;
                    _airborneReturnState = movingFall
                        ? LocomotionContext(previous)
                        : PlayerAnimationState.Idle;
                    Select(
                        state,
                        movingFall ? PlayerAvatarAnimationRole.MovingJump : PlayerAvatarAnimationRole.StationaryJump,
                        loop: false,
                        restart: true);
                }
                break;

            case PlayerAnimationState.Land:
                Select(state,
                    IsLocomotion(_airborneReturnState)
                        ? PlayerAvatarAnimationRole.SustainedLocomotion
                        : PlayerAvatarAnimationRole.Standing,
                    loop: true, restart: true);
                break;

            case PlayerAnimationState.Attack:
                _attackReturnState = GroundContext(previous);
                Select(state, PlayerAvatarAnimationRole.PrimaryAttack, loop: false, restart: true);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(state));
        }

        return Current;
    }

    private void CompleteLocomotionStartIfNeeded()
    {
        if (!IsLocomotion(Current.SemanticState) ||
            CurrentClip.Role != PlayerAvatarAnimationRole.LocomotionStart ||
            Current.ElapsedAt(_clockSeconds) < CurrentClip.DurationSeconds)
            return;

        double completedAt = Current.ClipStartedAtSeconds + CurrentClip.DurationSeconds;
        Select(Current.SemanticState, PlayerAvatarAnimationRole.SustainedLocomotion,
            loop: true, restart: true, completedAt);
    }

    private void CompleteAttackIfNeeded()
    {
        if (Current.SemanticState != PlayerAnimationState.Attack ||
            Current.ElapsedAt(_clockSeconds) < CurrentClip.DurationSeconds)
            return;

        double completedAt = Current.ClipStartedAtSeconds + CurrentClip.DurationSeconds;
        Select(_attackReturnState,
            IsLocomotion(_attackReturnState)
                ? PlayerAvatarAnimationRole.SustainedLocomotion
                : PlayerAvatarAnimationRole.Standing,
            loop: true, restart: true, completedAt);
    }

    private PlayerAnimationState GroundContext(PlayerAnimationState previous)
    {
        if (IsLocomotion(previous))
            return previous;
        if (CurrentClip.Role == PlayerAvatarAnimationRole.SustainedLocomotion && IsLocomotion(_airborneReturnState))
            return _airborneReturnState;
        return PlayerAnimationState.Idle;
    }

    private PlayerAnimationState LocomotionContext(PlayerAnimationState previous) =>
        IsLocomotion(previous)
            ? previous
            : IsLocomotion(_airborneReturnState)
                ? _airborneReturnState
                : PlayerAnimationState.Run;

    private PlayerAvatarAnimationClip CurrentClip =>
        _avatar.AnimationClips.Single(clip => string.Equals(clip.Id, Current.ClipId, StringComparison.Ordinal));

    private void Select(
        PlayerAnimationState state,
        PlayerAvatarAnimationRole role,
        bool loop,
        bool restart,
        double? startAtSeconds = null)
    {
        var target = _avatar.RequiredAnimationClip(role);
        double started = restart || !string.Equals(Current.ClipId, target.Id, StringComparison.Ordinal)
            ? startAtSeconds ?? _clockSeconds
            : Current.ClipStartedAtSeconds;
        Current = new PlayerAnimationPresentation(state, target.Id, started, loop);
    }

    private PlayerAnimationPresentation Presentation(
        PlayerAnimationState state,
        PlayerAvatarAnimationRole role,
        bool loop,
        double started) =>
        new(state, _avatar.RequiredAnimationClip(role).Id, started, loop);

    private static bool IsLocomotion(PlayerAnimationState state) =>
        state is PlayerAnimationState.Walk or PlayerAnimationState.Run;
}
