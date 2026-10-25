using OBP.Core;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC2.Gameplay;

/// <summary>
/// Retail-backed Aranos opening door. LEVEL0 class 2755 instance 166 is the
/// double door directly beyond the first lift. NTSC v1.01 establishes:
/// state 0 opens only when 3D player distance is strictly below 6.0;
/// sequence 1 has 30 frames at rate 0.5; completion sets native state 2 and
/// selects sequence 2, a one-frame latched-open pose.
/// </summary>
public sealed class Rac2AranosOpeningDoorSession
{
    public const int NativeClassId = 2755;
    public const int OpeningInstanceIndex = 166;
    public const double OpenTriggerDistance = 6.0;
    public const int OpeningSequenceFrames = 30;
    public const int NativeTicksPerSecond = 60;
    public const double OpeningFrameRate = 30.0;
    public const int OpeningNativeTicks = 60;

    private int _openingTicks;

    public Rac2AranosOpeningDoorSession(
        RuntimeDynamicObject source,
        RuntimeEntityState? initialState = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        if (source.SourceGame != ObpSourceGame.Rac2 ||
            source.NativeClassId != NativeClassId ||
            source.InstanceIndex != OpeningInstanceIndex)
        {
            throw new InvalidDataException(
                $"Expected Aranos class-{NativeClassId} instance {OpeningInstanceIndex}, got " +
                $"{source.SourceGame}:{source.NativeClassId}:{source.InstanceIndex}.");
        }

        EntityState = initialState ?? RuntimeEntityState.FromAuthored(source);
        EntityState.EnsureMatches(source);
    }

    public RuntimeDynamicObject Source { get; }
    public RuntimeEntityState EntityState { get; private set; }
    public Rac2AranosOpeningDoorPhase Phase { get; private set; }
        = Rac2AranosOpeningDoorPhase.Closed;
    public int NativeState => Phase switch
    {
        Rac2AranosOpeningDoorPhase.Closed => 0,
        Rac2AranosOpeningDoorPhase.Opening => 1,
        _ => 2,
    };
    public int OpeningTicks => _openingTicks;

    public bool ObservePlayerDistance(double playerDistance)
    {
        if (!double.IsFinite(playerDistance) || playerDistance < 0d)
            throw new ArgumentOutOfRangeException(nameof(playerDistance));

        if (Phase != Rac2AranosOpeningDoorPhase.Closed ||
            !(playerDistance < OpenTriggerDistance))
            return false;

        Phase = Rac2AranosOpeningDoorPhase.Opening;
        EntityState = EntityState.WithAnimationRole(RuntimeObjectAnimationRole.Reaction);
        return true;
    }

    public Rac2AranosOpeningDoorPhase AdvanceNativeTick()
    {
        if (Phase != Rac2AranosOpeningDoorPhase.Opening)
            return Phase;

        _openingTicks++;
        if (_openingTicks >= OpeningNativeTicks)
        {
            _openingTicks = OpeningNativeTicks;
            Phase = Rac2AranosOpeningDoorPhase.Open;
        }
        return Phase;
    }
}

public enum Rac2AranosOpeningDoorPhase
{
    Closed,
    Opening,
    Open,
}
