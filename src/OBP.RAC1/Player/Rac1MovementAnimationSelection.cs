namespace OBP.RAC1.Player;

/// <summary>
/// A source-owned traversal selector supplied by the movement session. Entry
/// generation distinguishes restarting the same action from continuing it.
/// Loop/terminal-pose policy is an explicit OBP presentation choice until the
/// corresponding native loop-window producer has been recovered.
/// </summary>
public readonly record struct Rac1MovementAnimationSelection(
    int NativeAction, int Sequence, long EntryGeneration, int EntryFrame = 0,
    int BlendTicks = 5, bool Loop = false, float PlaybackMultiplier = 1f,
    int? LoopStartFrame = null, int? LoopEndFrame = null, int? ExitLoopFrame = null);
