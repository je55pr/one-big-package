namespace OBP.Runtime.Player;

/// <summary>
/// Engine-neutral semantic presentation states for the player avatar.
/// Movement and physics remain authoritative; these states only describe how
/// the already-resolved movement should be presented.
/// </summary>
public enum PlayerAnimationState
{
    Idle,
    Walk,
    Run,
    JumpRise,
    Fall,
    Land,
    Attack,
}
