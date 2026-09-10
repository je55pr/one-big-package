using OBP.RAC1.Animation;
using OBP.RAC1.Geometry;
using OBP.RAC1.Level;

namespace OBP.RAC1.Player;

/// <summary>
/// Retail-backed, engine-independent R&amp;C1 Ratchet avatar data.
/// Geometry and animation remain in native Moby local space; no placement,
/// runtime axis remap, controller overlay, or presentation correction is applied.
/// </summary>
public static class Rac1RatchetAvatar
{
    public const int RatchetClassId = 0;
    public const int StandingSequenceId = 0;
    public const int LocomotionStartSequenceId = 3;
    public const int SustainedLocomotionSequenceId = 4;
    public const int LocomotionStopVariantASequenceId = 5;
    public const int LocomotionStopVariantBSequenceId = 6;
    public const int StationaryJumpSequenceId = 7;
    public const int MovingJumpSequenceId = 8;
    public const int CrouchSequenceId = 13;
    public const int CrouchTurnRightSequenceId = 14;
    public const int CrouchTurnLeftSequenceId = 15;
    public const int WrenchAttackSequenceId = 23;
    public const int BindAnchorSequenceId = 122;
    public const float NtscUpdateHz = 60f;

    public enum AxisDirection
    {
        PositiveX,
        PositiveY,
        PositiveZ,
    }

    public readonly record struct LocalPoint(double X, double Y, double Z);

    public sealed record LocalBounds(LocalPoint Min, LocalPoint Max)
    {
        public double Width => Max.X - Min.X;
        public double Depth => Max.Y - Min.Y;
        public double Height => Max.Z - Min.Z;
    }

    public sealed record Surface(int TextureId, int[] Indices);

    public sealed record AnimationClip(
        int SequenceId,
        IReadOnlyList<double[]> LocalFrames,
        IReadOnlyList<double> FrameDurationsSeconds)
    {
        public int FrameCount => LocalFrames.Count;
        public bool HasVariableTiming =>
            FrameDurationsSeconds.Count > 1 &&
            FrameDurationsSeconds.Skip(1).Any(duration => duration != FrameDurationsSeconds[0]);
        public float? ConstantFramesPerSecond => HasVariableTiming || FrameDurationsSeconds.Count == 0
            ? null
            : (float)(1d / FrameDurationsSeconds[0]);
    }

    public sealed record Asset(
        Rac1Moby.Mesh Mesh,
        IReadOnlyList<Surface> Surfaces,
        IReadOnlyList<AnimationClip> AnimationClips,
        IReadOnlyList<Rac1Moby.SkeletonJoint> Skeleton,
        LocalBounds RestBounds,
        LocalBounds StandingBounds,
        LocalPoint Origin,
        double BaseHeightZ,
        AxisDirection RightAxis,
        AxisDirection ForwardAxis,
        AxisDirection UpAxis)
    {
        public int ClassId => RatchetClassId;
        public int JointCount => Mesh.JointCount;
        public IReadOnlyList<int> TextureIds => Surfaces.Select(surface => surface.TextureId).ToArray();
        public AnimationClip Clip(int sequenceId) => AnimationClips.Single(clip => clip.SequenceId == sequenceId);
        public IReadOnlyList<double[]> StandingFrames => Clip(StandingSequenceId).LocalFrames;
        public float FramesPerSecond => Clip(StandingSequenceId).ConstantFramesPerSecond ?? 0f;
    }

    public static Asset Decode(Rac1LevelCore.Core core)
    {
        var classes = Rac1StaticClasses.Read(core);
        if (!classes.Mobies.TryGetValue(RatchetClassId, out var cls))
            throw new InvalidDataException("R&C1 Ratchet class 0 is missing.");

        if (cls.JointCount != 111 || cls.Joints.Count != 111 ||
            cls.Mesh.Positions.Length != 5_583 * 3 ||
            cls.Mesh.Indices.Length != 6_856 * 3 ||
            cls.Mesh.Uvs.Length != 5_583 * 2 ||
            cls.TriangleTextureIds.Length != 6_856)
        {
            throw new InvalidDataException(
                "R&C1 Ratchet class 0 no longer matches the pinned retail body invariants.");
        }

        var sequences = Rac1MobyAnimation.ReadRatchetSequences(
            core.Assets,
            core.Index,
            core.Header.RatchetSequencesOffset,
            cls.JointCount);

        var bind = sequences[BindAnchorSequenceId].Value
            ?? throw new InvalidDataException("R&C1 Ratchet bind-anchor sequence 122 is absent.");
        if (bind.Frames.Count != 21 || bind.ConstantTransitionRate != 0.5f ||
            !Rac1MobyPose.CanPoseRatchetHierarchy(cls.Mesh, cls.Joints, bind.Frames[0]) ||
            !Rac1MobyPose.IsRatchetHierarchyBindLinearAnchor(cls.Joints, bind.Frames[0]))
        {
            throw new InvalidDataException(
                "R&C1 Ratchet sequence 122 no longer matches the pinned bind anchor.");
        }

        (int SequenceId, int FrameCount, uint? ConstantRateRaw)[] clipSpecs =
        [
            (StandingSequenceId, 10, 0x3e000000u),
            (LocomotionStartSequenceId, 33, 0x3e800000u),
            (SustainedLocomotionSequenceId, 23, 0x3f000000u),
            (LocomotionStopVariantASequenceId, 13, 0x3e800000u),
            (LocomotionStopVariantBSequenceId, 13, 0x3e800000u),
            (StationaryJumpSequenceId, 29, null),
            (MovingJumpSequenceId, 16, null),
            (CrouchSequenceId, 15, 0x3e800000u),
            (CrouchTurnRightSequenceId, 7, 0x3e800000u),
            (CrouchTurnLeftSequenceId, 7, 0x3e800000u),
            (WrenchAttackSequenceId, 21, null),
        ];
        var clips = clipSpecs
            .Select(spec => DecodeClip(cls, sequences, spec.SequenceId, spec.FrameCount, spec.ConstantRateRaw))
            .ToArray();
        var standing = clips.Single(clip => clip.SequenceId == StandingSequenceId);

        var surfaceIndices = new SortedDictionary<int, List<int>>();
        for (int face = 0; face < cls.TriangleTextureIds.Length; face++)
        {
            int textureId = cls.TriangleTextureIds[face];
            if (textureId < 0)
                throw new InvalidDataException("R&C1 Ratchet contains an untextured retail face.");

            if (!surfaceIndices.TryGetValue(textureId, out var indices))
                surfaceIndices[textureId] = indices = [];

            indices.Add(cls.Mesh.Indices[face * 3]);
            indices.Add(cls.Mesh.Indices[face * 3 + 1]);
            indices.Add(cls.Mesh.Indices[face * 3 + 2]);
        }

        var surfaces = surfaceIndices
            .Select(pair => new Surface(pair.Key, pair.Value.ToArray()))
            .ToArray();
        var restBounds = Measure(cls.Mesh.Positions);
        var standingBounds = Measure(standing.LocalFrames.SelectMany(frame => frame));

        // Native RAC1 Moby space is Z-up. Placement yaw is the native Z rotation,
        // with the unrotated heading carried on +Y. Adapters may remap axes later.
        return new Asset(
            cls.Mesh,
            surfaces,
            clips,
            cls.Joints,
            restBounds,
            standingBounds,
            new LocalPoint(0, 0, 0),
            standingBounds.Min.Z,
            AxisDirection.PositiveX,
            AxisDirection.PositiveY,
            AxisDirection.PositiveZ);
    }

    private static AnimationClip DecodeClip(
        Rac1StaticClasses.MobyClass cls,
        IReadOnlyList<Rac1MobyAnimation.SequenceSlot> sequences,
        int sequenceId,
        int expectedFrameCount,
        uint? expectedConstantRateRaw)
    {
        var sequence = sequences[sequenceId].Value
            ?? throw new InvalidDataException($"R&C1 Ratchet sequence {sequenceId} is absent.");
        if (sequence.Frames.Count != expectedFrameCount)
            throw new InvalidDataException(
                $"R&C1 Ratchet sequence {sequenceId} frame count drifted from {expectedFrameCount}.");

        if (expectedConstantRateRaw is uint constantRateRaw)
        {
            if (sequence.ConstantTransitionRateRaw != constantRateRaw)
                throw new InvalidDataException(
                    $"R&C1 Ratchet sequence {sequenceId} constant timing no longer matches retail evidence.");
        }
        else if (sequence.ConstantTransitionRateRaw != 0 ||
                 !sequence.Frames.Select(frame => frame.TransitionRateRaw).Distinct().Skip(1).Any())
        {
            throw new InvalidDataException(
                $"R&C1 Ratchet sequence {sequenceId} no longer has the admitted variable timing.");
        }

        if (sequence.Frames.Any(frame =>
            !Rac1MobyPose.CanPoseRatchetHierarchy(cls.Mesh, cls.Joints, frame)))
        {
            throw new InvalidDataException(
                $"R&C1 Ratchet sequence {sequenceId} can no longer pose the pinned hierarchy.");
        }

        var frames = sequence.Frames
            .Select(frame => Rac1MobyPose.PoseRatchetHierarchy(cls.Mesh, cls.Joints, frame))
            .ToArray();
        double[] durations;
        if (expectedConstantRateRaw is not null)
        {
            double duration = FrameDuration(sequence.ConstantTransitionRate, sequenceId);
            durations = Enumerable.Repeat(duration, sequence.Frames.Count).ToArray();
        }
        else
        {
            durations = sequence.Frames
                .Select(frame => FrameDuration(frame.TransitionRate, sequenceId))
                .ToArray();
        }

        return new AnimationClip(sequenceId, frames, durations);
    }

    private static double FrameDuration(float transitionRate, int sequenceId)
    {
        if (!(transitionRate > 0) || !float.IsFinite(transitionRate))
            throw new InvalidDataException(
                $"R&C1 Ratchet sequence {sequenceId} has invalid frame transition rate {transitionRate}.");
        return 1d / (transitionRate * NtscUpdateHz);
    }

    private static LocalBounds Measure(IEnumerable<double> positions)
    {
        double[] values = positions as double[] ?? positions.ToArray();
        if (values.Length == 0 || values.Length % 3 != 0)
            throw new InvalidDataException("R&C1 Ratchet position stream is not xyz-aligned.");
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        for (int i = 0; i < values.Length; i += 3)
        {
            double x = values[i], y = values[i + 1], z = values[i + 2];
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
                throw new InvalidDataException("R&C1 Ratchet position stream contains a non-finite value.");

            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            minZ = Math.Min(minZ, z);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
            maxZ = Math.Max(maxZ, z);
        }

        return new LocalBounds(
            new LocalPoint(minX, minY, minZ),
            new LocalPoint(maxX, maxY, maxZ));
    }
}
