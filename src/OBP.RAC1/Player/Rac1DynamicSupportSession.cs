using OBP.RAC1.Gameplay;

namespace OBP.RAC1.Player;

/// <summary>
/// Host-supplied native-equivalent dynamic-contact facts for one player tick.
/// Geometry selection remains host-owned. The common runtime owns only support
/// history, carry derivation, and the separation of current contact from the
/// persistent support pointer recovered in the retail player state.
/// </summary>
public sealed record Rac1DynamicSupportFacts(
    bool IsGrounded,
    bool HitCeiling,
    int? RawFaceType,
    Rac1MobyRuntimeKey? ContactedMoby,
    Rac1MobyRuntimeKey? CurrentDynamicContact,
    Rac1MobyRuntimeKey? PersistentSupportMoby,
    Rac1SupportAnchorState SupportAnchor,
    Rac1NativeVector3? SupportAnchorWorldPosition,
    Rac1ConveyorTransfer Conveyor)
{
    public Rac1DynamicSupportFacts Validate()
    {
        if (RawFaceType is < byte.MinValue or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(RawFaceType));

        bool hasPersistentSupport = PersistentSupportMoby is not null;
        bool hasAnchorSample = SupportAnchorWorldPosition.HasValue;
        if (SupportAnchor.IsValid &&
            (!hasPersistentSupport || !hasAnchorSample))
        {
            throw new ArgumentException(
                "A valid R&C1 support anchor requires both persistent support identity and an anchor world-position sample.");
        }
        if (!SupportAnchor.IsValid && hasAnchorSample)
        {
            throw new ArgumentException(
                "An invalid R&C1 support anchor cannot carry a world-position sample.");
        }

        if (SupportAnchorWorldPosition is { } anchor &&
            (!double.IsFinite(anchor.X) ||
             !double.IsFinite(anchor.Y) ||
             !double.IsFinite(anchor.Z)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(SupportAnchorWorldPosition));
        }

        return this;
    }
}

/// <summary>
/// Stateful producer for the recovered dynamic-support fields. A first sample or
/// support switch establishes history with zero carry. Consecutive samples for
/// the same persistent support yield the native-space anchor transform delta.
/// Invalid support clears history. Conveyor transfer is never folded into this
/// history and remains a separate channel on the contact result.
/// </summary>
public sealed class Rac1DynamicSupportSession
{
    private Rac1MobyRuntimeKey? _previousSupport;
    private Rac1NativeVector3? _previousAnchorWorldPosition;

    public Rac1MobyRuntimeKey? PreviousSupport => _previousSupport;
    public Rac1NativeVector3? PreviousAnchorWorldPosition =>
        _previousAnchorWorldPosition;

    public void Reset()
    {
        _previousSupport = null;
        _previousAnchorWorldPosition = null;
    }

    public Rac1PlayerContactResult StepStatic(
        bool isGrounded,
        bool hitCeiling = false,
        int? rawFaceType = null)
    {
        Reset();
        return Rac1PlayerContactResult.StaticWorld(
            isGrounded,
            hitCeiling,
            rawFaceType);
    }

    public Rac1PlayerContactResult Step(Rac1DynamicSupportFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        facts.Validate();

        Rac1SupportCarry carry = Rac1SupportCarry.None;
        if (facts.SupportAnchor.IsValid &&
            facts.PersistentSupportMoby is { } support &&
            facts.SupportAnchorWorldPosition is { } anchor)
        {
            if (_previousSupport == support &&
                _previousAnchorWorldPosition is { } previous)
            {
                carry = new Rac1SupportCarry(new Rac1NativeVector3(
                    anchor.X - previous.X,
                    anchor.Y - previous.Y,
                    anchor.Z - previous.Z));
            }

            _previousSupport = support;
            _previousAnchorWorldPosition = anchor;
        }
        else
        {
            Reset();
        }

        return new Rac1PlayerContactResult(
            facts.IsGrounded,
            facts.HitCeiling,
            facts.RawFaceType.HasValue
                ? Rac1CollisionFaceSemantics.Decode(facts.RawFaceType.Value)
                : null,
            facts.ContactedMoby,
            facts.CurrentDynamicContact,
            facts.PersistentSupportMoby,
            facts.SupportAnchor,
            carry,
            facts.Conveyor);
    }
}
