using OBP.Core;
using OBP.RAC1.Animation;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Retail-backed Veldin red-plant proximity reaction.  The native selector and
/// 0.5-frame/update sequence rate are recovered; the host supplies player
/// motion/distance and infers the animation-finished flag from the source clip
/// duration because live Moby+0x70 is not available in the Godot runtime.
/// </summary>
public sealed class Rac1RedPlantSession
{
    public const int NativeLevelId = 0;
    public const int ReactionFrameCount = 20;
    public const int ReactionTicks = ReactionFrameCount * 2;

    private readonly Dictionary<int, int> _reactionTicks = [];

    public int RegisteredCount => _reactionTicks.Count;

    public bool Register(RuntimeDynamicObject source)
    {
        if (source.SourceGame != ObpSourceGame.Rac1 || source.NativeClassId != Rac1MobyAnimationProvider.RedPlantClassId)
            return false;
        if (source.Animations?.Find(RuntimeObjectAnimationRole.Reaction) is not { } clip ||
            clip.FrameCount != ReactionFrameCount ||
            clip.ConstantFramesPerSecond != 30f)
            throw new InvalidDataException("R&C1 red-plant source reaction clip is not the recovered 20-frame, 30-FPS asset.");
        if (!_reactionTicks.TryAdd(source.InstanceIndex, 0))
            throw new InvalidOperationException($"Duplicate R&C1 red-plant instance {source.InstanceIndex}.");
        return true;
    }

    /// <summary>Run one 60 Hz native update. Returns the entity's neutral state.</summary>
    public RuntimeEntityState Step(RuntimeDynamicObject source, RuntimeEntityState state,
        double playerDistance, double playerMotionPerTick)
    {
        state.EnsureMatches(source);
        if (!_reactionTicks.TryGetValue(source.InstanceIndex, out int ticks))
            throw new InvalidOperationException("Red plant is not registered.");

        byte flags = 0;
        if (state.Presentation.AnimationRole == RuntimeObjectAnimationRole.Reaction)
        {
            ticks++;
            if (ticks >= ReactionTicks)
                flags = Rac1MobyAnimationProvider.RedPlantReturnToRestFlag;
        }
        var next = Rac1MobyAnimationProvider.AdvanceRedPlantEntityState(
            source, state, playerDistance, playerMotionPerTick, flags);
        if (next.Presentation.AnimationRole != RuntimeObjectAnimationRole.Reaction)
            ticks = 0;
        _reactionTicks[source.InstanceIndex] = ticks;
        return next;
    }
}
