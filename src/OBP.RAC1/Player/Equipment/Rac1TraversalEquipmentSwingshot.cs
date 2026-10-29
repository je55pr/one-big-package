using OBP.Core;
using System.Buffers.Binary;
using OBP.Runtime;

namespace OBP.RAC1.Player;

public enum Rac1TraversalEquipmentSwingshotMode
{
    Pull,
    Swing,
}

/// <summary>
/// Ordinary class-758 Versa-Target fields consumed by 0x222158 and the held
/// Swingshot updater. Class 803's separate target family is not this contract.
/// </summary>
public readonly record struct Rac1TraversalEquipmentSwingshotTarget(
    Rac1TraversalEquipmentSwingshotMode Mode,
    float Radius,
    float MaximumDistance,
    int RegionIndex)
{
    public const int NativeMobyClass = 0x2f6;
    public const float DefaultMaximumDistance = 30f;
    public const float MinimumPlanarDistance = 4f;
    public const float FacingConeRadians = 0.5235988f;

    public bool HasUnrestrictedRegion => RegionIndex < 0;

    /// <summary>
    /// Interpret source payloads on the source-game side of the runtime boundary.
    /// Imported matrices use Y up; the emitted anchor restores native Z up.
    /// Live transform/enable changes must be supplied separately by their owner.
    /// </summary>
    public static Rac1TraversalEquipmentSwingshotAuthoredTarget? ReadAuthored(RuntimeDynamicObject source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SourceGame != ObpSourceGame.Rac1 || source.NativeClassId != NativeMobyClass)
            return null;
        var payloads = source.NativePayloads?.Where(p => p.Format == "rac1-pvar").ToArray() ?? [];
        if (payloads.Length != 1 || !TryDecode(source.NativeClassId, payloads[0].Data, out var target))
            return null;
        double[] matrix = source.Transform.Matrix;
        if (matrix.Length != 16 || !matrix.All(double.IsFinite))
            throw new InvalidDataException("R&C1 Versa-Target has an invalid runtime transform.");
        return new(source.InstanceIndex, target, new(matrix[12], matrix[14], matrix[13]));
    }

    /// <summary>
    /// PVar+0 must be zero for the ordinary target producer. Live state flags,
    /// region admission and line of sight remain independently supplied facts.
    /// </summary>
    public static bool TryDecode(int nativeMobyClass, ReadOnlySpan<byte> pvar,
        out Rac1TraversalEquipmentSwingshotTarget target)
    {
        target = default;
        if (nativeMobyClass != NativeMobyClass || pvar.Length < 0x24)
            return false;
        if (Word(pvar, 0) != 0)
            return false;
        float radius = BitConverter.Int32BitsToSingle(Word(pvar, 4));
        float maximumDistance = BitConverter.Int32BitsToSingle(Word(pvar, 0x20));
        if (!float.IsFinite(radius) || radius < 0 || !float.IsFinite(maximumDistance) || maximumDistance < 0)
            return false;
        target = new(
            Word(pvar, 8) == 0 ? Rac1TraversalEquipmentSwingshotMode.Pull : Rac1TraversalEquipmentSwingshotMode.Swing,
            radius,
            maximumDistance == 0 ? DefaultMaximumDistance : maximumDistance,
            Word(pvar, 0x1c));
        return true;
    }

    private static int Word(ReadOnlySpan<byte> pvar, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(pvar.Slice(offset, 4));

    /// <summary>
    /// Non-aiming action's geometric candidate gate. Native state/visibility flags,
    /// authored-region helper and the Z+0.5 ray are external collision facts.
    /// Score is the native distance * (1 + absolute yaw error), plus visibility
    /// and priority penalties. Callers choose the strictly lowest score.
    /// </summary>
    public double? CandidateScore(Rac1NativeVector3 player, Rac1NativeVector3 anchor,
        double facingYaw, bool enabled, bool regionAdmitted, bool hasLineOfSight,
        bool recentlyVisible = true, bool priorityPenalty = false)
    {
        Validate(player);
        Validate(anchor);
        if (!double.IsFinite(facingYaw))
            throw new ArgumentOutOfRangeException(nameof(facingYaw));
        if (!enabled || !regionAdmitted || !hasLineOfSight)
            return null;
        double x = anchor.X - player.X;
        double y = anchor.Y - player.Y;
        double z = anchor.Z - player.Z;
        double planar = Math.Sqrt(x * x + y * y);
        double distance = Math.Sqrt(planar * planar + z * z);
        double yawError = Math.Abs(Rac1RatchetYawController.WrapPi(Math.Atan2(y, x) - facingYaw));
        if (distance > MaximumDistance || planar < MinimumPlanarDistance || yawError > FacingConeRadians)
            return null;
        return distance * (1 + yawError) + (recentlyVisible ? 0 : 10) + (priorityPenalty ? 7 : 0);
    }

    private static void Validate(Rac1NativeVector3 vector)
    {
        if (!double.IsFinite(vector.X) || !double.IsFinite(vector.Y) ||
            !double.IsFinite(vector.Z) || !double.IsFinite(vector.Length))
            throw new ArgumentOutOfRangeException(nameof(vector));
    }
}

public readonly record struct Rac1TraversalEquipmentSwingshotAuthoredTarget(
    int InstanceIndex,
    Rac1TraversalEquipmentSwingshotTarget Target,
    Rac1NativeVector3 NativeAnchor);

/// <summary>
/// Native action-0x26 tether constraint, Veldin 0x219d74..0x219e70.
/// Input P+0x110 is contact-processed native motion, not host velocity or external
/// platform carry. This emits the request before the source contact solver.
/// </summary>
public static class Rac1TraversalEquipmentSwingshot
{
    public const int PullAction = 0x25;
    public const int SwingAction = 0x26;
    public const int ReadyAction = 0x24;
    public const int SwingSequence = 0x30;
    public const int SwingEntryBlendTicks = 5;
    public const double SecondsPerUpdate = 1d / 60d;
    public const double RadiusChangePerUpdate = 10d * SecondsPerUpdate;
    public const double ContactMotionRetention = 0.9f;
    public const double GravityStep = 15d * SecondsPerUpdate * SecondsPerUpdate;

    public readonly record struct SwingStep(Rac1NativeVector3 Request, double Radius);

    public static double InitialSwingRadius(Rac1NativeVector3 position, Rac1NativeVector3 anchor)
    {
        Validate(position);
        Validate(anchor);
        return new Rac1NativeVector3(position.X - anchor.X, position.Y - anchor.Y, position.Z - anchor.Z).Length;
    }

    /// <summary>
    /// Action-26 release uses source support distance P+0x2dc. Full contact
    /// momentum is copied by fall action 6's entry, independently of scalar speed.
    /// </summary>
    public static int ReleaseAction(double nativeSupportDistance)
    {
        if (!double.IsFinite(nativeSupportDistance) || nativeSupportDistance < 0)
            throw new ArgumentOutOfRangeException(nameof(nativeSupportDistance));
        return nativeSupportDistance < (double)0.4f ? 0 : 6;
    }

    public static SwingStep StepSwing(Rac1NativeVector3 position, Rac1NativeVector3 anchor,
        Rac1NativeVector3 contactMotion, double radius, double targetRadius)
    {
        Validate(position);
        Validate(anchor);
        Validate(contactMotion);
        if (!double.IsFinite(radius) || radius < 0)
            throw new ArgumentOutOfRangeException(nameof(radius));
        if (!double.IsFinite(targetRadius) || targetRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(targetRadius));
        radius += Math.Clamp(targetRadius - radius, -RadiusChangePerUpdate, RadiusChangePerUpdate);
        var predicted = new Rac1NativeVector3(
            position.X + contactMotion.X * ContactMotionRetention,
            position.Y + contactMotion.Y * ContactMotionRetention,
            position.Z + contactMotion.Z * ContactMotionRetention - GravityStep);
        var radial = new Rac1NativeVector3(
            predicted.X - anchor.X, predicted.Y - anchor.Y, predicted.Z - anchor.Z);
        double scale = radial.Length == 0 ? 0 : radius / radial.Length;
        var corrected = new Rac1NativeVector3(
            anchor.X + radial.X * scale, anchor.Y + radial.Y * scale, anchor.Z + radial.Z * scale);
        return new(new(corrected.X - position.X, corrected.Y - position.Y, corrected.Z - position.Z), radius);
    }

    private static void Validate(Rac1NativeVector3 vector)
    {
        if (!double.IsFinite(vector.X) || !double.IsFinite(vector.Y) ||
            !double.IsFinite(vector.Z) || !double.IsFinite(vector.Length))
            throw new ArgumentOutOfRangeException(nameof(vector));
    }
}
