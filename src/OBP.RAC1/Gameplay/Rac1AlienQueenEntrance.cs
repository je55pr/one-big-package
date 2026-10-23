using System.Buffers.Binary;
using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// FUN_L06_002f9a28: authenticated native class1051 state1 entrance and
/// state2->7->8 handoff. Linked class1021 activation, native camera and
/// FUN_L06_002fc6d8 landing remain explicit external observations, not
/// invented player-distance or elapsed-time shortcuts.
/// </summary>
public sealed class Rac1AlienQueenEntrance
{
    public const int ClassId = 1051;
    public const int LinkedClassId = 1021;
    public const int StartupDelayTicks = 10;
    public const float InitialHeightAboveAuthored = 17.5f;
    public const float GravityPerNativeTick = 30f / 3600f;
    private bool _armed;
    private int _remaining;
    private bool _linkedCommitted;
    private bool _airborneLandingObserved;

    public Rac1AlienQueenEntrance(RuntimeDynamicObject queen, IReadOnlyDictionary<int, int> authoredClasses)
    {
        ArgumentNullException.ThrowIfNull(queen);
        ArgumentNullException.ThrowIfNull(authoredClasses);
        if (queen.SourceGame != "rac1" || queen.NativeClassId != ClassId)
            throw new ArgumentException("Requires retail class1051 Queen.", nameof(queen));
        var pvar = queen.NativePayloads?.SingleOrDefault(p =>
            p.Format == Rac1BoltCrate.PVarPayloadFormat)?.Data;
        if (pvar is not { Length: >= 0x108 })
            throw new InvalidDataException("Queen entrance requires original linked-actor selectors.");
        int linkA = BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0xfc));
        int linkB = BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0x100));
        if (linkA == linkB || !authoredClasses.TryGetValue(linkA, out int classA) ||
            !authoredClasses.TryGetValue(linkB, out int classB) ||
            classA != LinkedClassId || classB != LinkedClassId)
            throw new InvalidDataException("Queen entrance references invalid native class1021 links.");
        QueenInstance = queen.InstanceIndex;
        FirstLinkedInstance = linkA;
        SecondLinkedInstance = linkB;
        LinkedEventSelector = BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0x104));
        CameraSelector = BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0xf8));
        NativePathSelector = BinaryPrimitives.ReadInt32LittleEndian(pvar.AsSpan(0xec));
    }

    public int QueenInstance { get; }
    public int FirstLinkedInstance { get; }
    public int SecondLinkedInstance { get; }
    public int LinkedEventSelector { get; }
    public int CameraSelector { get; }
    public int NativePathSelector { get; }
    public int NativeState { get; private set; } = 1;
    public int RemainingTicks => _remaining;
    public bool LinkedActivationRequired => _armed && _remaining == 0 && !_linkedCommitted && NativeState == 1;

    /// <summary>
    /// The native target is class0 Ratchet (moby+0xa6=0). The other guard is
    /// Queen PVar +0xb4 being 2. The recovery helper decrements +0x12c
    /// before state1 each tick once +0x170 has been set.
    /// A successful link acknowledgement represents the two actual class1021
    /// callbacks and native event selector, *not* an assumed cutscene.
    /// </summary>
    public Rac1AlienQueenEntranceTick StepWaiting(bool targetIsNativeClassZero,
        bool sourceTargetModeIsTwo, bool linkedActorAndCameraCallbacksCommitted = false)
    {
        if (NativeState != 1) return Snapshot();
        // FUN_L06_002fa9c0 runs BEFORE the class1051 state switch and
        // decrements +0x12c even if state1 subsequently exits at +0xb4
        // or the target-class guard. Only starting the timer is gated.
        if (_armed && _remaining > 0) _remaining--;
        if (!targetIsNativeClassZero || sourceTargetModeIsTwo) return Snapshot();
        if (!_armed)
        {
            _armed = true;
            _remaining = StartupDelayTicks;
            return Snapshot();
        }
        if (_remaining == 0 && linkedActorAndCameraCallbacksCommitted)
        {
            _linkedCommitted = true;
            NativeState = 2;
        }
        return Snapshot();
    }

    /// <summary>The landing check is FUN_L06_002fc6d8, whose collision
    /// producer is not duplicated here. State7 waits while target mode=2;
    /// a separate valid target mode then admits combat state8.</summary>
    public Rac1AlienQueenEntranceTick ObserveLanding(bool nativeLandingConfirmed,
        bool sourceTargetModeIsTwo)
    {
        if (NativeState == 2 && nativeLandingConfirmed)
        {
            _airborneLandingObserved = true;
            NativeState = 7;
        }
        else if (NativeState == 7 && _airborneLandingObserved && !sourceTargetModeIsTwo)
            NativeState = 8;
        return Snapshot();
    }

    private Rac1AlienQueenEntranceTick Snapshot() => new(NativeState,
        _remaining, LinkedActivationRequired, _linkedCommitted, _airborneLandingObserved);
}

public sealed record Rac1AlienQueenEntranceTick(int NativeState, int RemainingTicks,
    bool RequiresLinkedActivation, bool LinkedCallbacksCommitted, bool LandingConfirmed);
