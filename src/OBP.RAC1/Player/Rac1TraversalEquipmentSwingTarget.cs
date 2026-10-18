using System.Buffers.Binary;
using OBP.Runtime;

namespace OBP.RAC1.Player;

/// <summary>Class-803 target admitted by 0x221ee0 and player action 0x2c.</summary>
public readonly record struct Rac1TraversalEquipmentSwingTarget(
    float Radius,
    float MaximumRequestLength,
    float MaximumDistance,
    int RegionIndex,
    float RadiusGain,
    float RadiusDamping,
    float RadiusMaximumStep,
    bool FollowsTargetFacing)
{
    public const int NativeMobyClass = 0x323;
    public bool HasUnrestrictedRegion => RegionIndex < 0;

    public static bool TryDecode(int nativeMobyClass, ReadOnlySpan<byte> pvar,
        out Rac1TraversalEquipmentSwingTarget target)
    {
        target = default;
        if (nativeMobyClass != NativeMobyClass || pvar.Length < 0x38 || Word(pvar, 0) == 0)
            return false;
        float radius = Float(pvar, 4);
        float maximumRequest = Float(pvar, 0xc);
        float distance = Float(pvar, 0x20);
        float gain = Float(pvar, 0x24);
        float damping = Float(pvar, 0x28);
        float maximumStep = Float(pvar, 0x2c);
        if (!float.IsFinite(radius) || radius <= 0 || !float.IsFinite(maximumRequest) || maximumRequest < 0 ||
            !float.IsFinite(distance) || distance < 0 || !float.IsFinite(gain) || gain < 0 ||
            !float.IsFinite(damping) || damping < 0 || !float.IsFinite(maximumStep) || maximumStep < 0)
            return false;
        target = new(radius, maximumRequest, distance == 0 ? 16 : distance, Word(pvar, 0x1c),
            gain == 0 ? 0.025f : gain, damping == 0 ? 0.33f : damping,
            maximumStep == 0 ? 11f / 60f : maximumStep, Word(pvar, 0x34) != 0);
        return true;
    }

    public static Rac1TraversalEquipmentSwingAuthoredTarget? ReadAuthored(RuntimeDynamicObject source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SourceGame != "rac1" || source.NativeClassId != NativeMobyClass)
            return null;
        var payloads = source.NativePayloads?.Where(p => p.Format == "rac1-pvar").ToArray() ?? [];
        if (payloads.Length != 1 || !TryDecode(source.NativeClassId, payloads[0].Data, out var target))
            return null;
        double[] matrix = source.Transform.Matrix;
        if (matrix.Length != 16 || !matrix.All(double.IsFinite))
            throw new InvalidDataException("R&C1 swing target has an invalid runtime transform.");
        return new(source.InstanceIndex, target, new(matrix[12], matrix[14], matrix[13]));
    }

    /// <summary>
    /// Ordinary non-aiming candidate geometry from 0x221ee0. Live flags,
    /// native region and player-Z+0.5 line-of-sight admission are external facts.
    /// </summary>
    public double? CandidateScore(Rac1NativeVector3 position, Rac1NativeVector3 anchor,
        double facingYaw, bool enabled, bool regionAdmitted, bool hasLineOfSight,
        bool recentlyVisible = true, bool priorityPenalty = false)
    {
        Validate(position);
        Validate(anchor);
        if (!double.IsFinite(facingYaw))
            throw new ArgumentOutOfRangeException(nameof(facingYaw));
        if (!enabled || !regionAdmitted || !hasLineOfSight || position.Z - anchor.Z > 3)
            return null;
        double x = anchor.X - position.X;
        double y = anchor.Y - position.Y;
        double z = anchor.Z - position.Z;
        double planar = Math.Sqrt(x * x + y * y);
        double distance = Math.Sqrt(planar * planar + z * z);
        double yaw = Math.Abs(Rac1RatchetYawController.WrapPi(Math.Atan2(y, x) - facingYaw));
        double pitch = Math.Abs(Math.Atan2(z, planar));
        if (distance > MaximumDistance || yaw > (double)0.7853982f || pitch > (double)0.7853982f)
            return null;
        return distance * (1 + yaw) + (recentlyVisible ? 0 : 10) + (priorityPenalty ? 7 : 0);
    }

    private static int Word(ReadOnlySpan<byte> pvar, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(pvar.Slice(offset, 4));
    private static float Float(ReadOnlySpan<byte> pvar, int offset) =>
        BitConverter.Int32BitsToSingle(Word(pvar, offset));
    private static void Validate(Rac1NativeVector3 vector)
    {
        if (!double.IsFinite(vector.X) || !double.IsFinite(vector.Y) ||
            !double.IsFinite(vector.Z) || !double.IsFinite(vector.Length))
            throw new ArgumentOutOfRangeException(nameof(vector));
    }
}

public readonly record struct Rac1TraversalEquipmentSwingAuthoredTarget(
    int InstanceIndex,
    Rac1TraversalEquipmentSwingTarget Target,
    Rac1NativeVector3 NativeAnchor);
