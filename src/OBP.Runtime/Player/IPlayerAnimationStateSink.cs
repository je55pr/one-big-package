namespace OBP.Runtime.Player;

/// <summary>
/// Presentation-side sink for the engine-neutral player animation state.
/// Movement/physics remain authoritative; implementations may choose native
/// clips or transitions without leaking source-game sequence IDs into gameplay.
/// </summary>
public interface IPlayerAnimationStateSink
{
    PlayerAnimationState CurrentAnimationState { get; }

    void SetAnimationState(PlayerAnimationState state);
}
