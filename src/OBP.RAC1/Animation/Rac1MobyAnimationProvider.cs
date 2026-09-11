using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.RAC1.Animation;

/// <summary>
/// Evidence-backed non-player R&amp;C1 animation admissions. Native sequence ids,
/// selector flags and thresholds stop here; callers receive neutral roles/clips.
/// </summary>
public static class Rac1MobyAnimationProvider
{
    public const int RedPlantClassId = 1781;
    public const double RedPlantTriggerDistance = 1.0;
    public const double RedPlantMinimumPlayerMotionPerTick = 2.0 / 60.0;
    public const byte RedPlantReturnToRestFlag = 0x02;

    private const int RestSequenceId = 0;
    private const int ReactionSequenceId = 1;
    private const float NtscUpdateHz = 60f;

    public sealed record RedPlantSelectorObservation(
        RuntimeObjectAnimationRole CurrentRole,
        double PlayerDistance,
        double PlayerMotionPerTick,
        byte NativeAnimationFlags);

    /// <summary>
    /// Replays the recovered Veldin red-plant selector ordering without exposing
    /// its native sequence ids to the neutral runtime.
    /// </summary>
    public static RuntimeObjectAnimationRole SelectRedPlantRole(RedPlantSelectorObservation state)
    {
        if (!double.IsFinite(state.PlayerDistance) || state.PlayerDistance < 0)
            throw new ArgumentOutOfRangeException(nameof(state), "Player distance must be finite and non-negative.");
        if (!double.IsFinite(state.PlayerMotionPerTick) || state.PlayerMotionPerTick < 0)
            throw new ArgumentOutOfRangeException(nameof(state), "Player motion must be finite and non-negative.");

        var role = state.CurrentRole;
        if (state.PlayerDistance < RedPlantTriggerDistance &&
            state.PlayerMotionPerTick > RedPlantMinimumPlayerMotionPerTick &&
            role != RuntimeObjectAnimationRole.Reaction)
        {
            role = RuntimeObjectAnimationRole.Reaction;
        }
        if ((state.NativeAnimationFlags & RedPlantReturnToRestFlag) != 0 &&
            role == RuntimeObjectAnimationRole.Reaction)
        {
            role = RuntimeObjectAnimationRole.Rest;
        }
        return role;
    }

    /// <summary>
    /// Apply the recovered red-plant selector to a neutral live entity snapshot.
    /// R&C1 native flags/thresholds remain game-owned; only the neutral animation
    /// role crosses into shared runtime state.
    /// </summary>
    public static RuntimeEntityState AdvanceRedPlantEntityState(
        RuntimeDynamicObject source, RuntimeEntityState current,
        double playerDistance, double playerMotionPerTick, byte nativeAnimationFlags)
    {
        if (source.SourceGame != "rac1" || source.NativeClassId != RedPlantClassId)
            throw new ArgumentException("Source is not an admitted R&C1 red plant.", nameof(source));
        current.EnsureMatches(source);
        var role = SelectRedPlantRole(new RedPlantSelectorObservation(
            current.Presentation.AnimationRole, playerDistance, playerMotionPerTick, nativeAnimationFlags));
        return current.WithAnimationRole(role);
    }

    /// <summary>
    /// Builds the only currently admitted non-player R&amp;C1 object clip. Other
    /// classes remain static even when their asset happens to contain sequences.
    /// </summary>
    public static RuntimeObjectAnimationSet? BuildAdmittedAnimationSet(
        Rac1StaticClasses.MobyClass cls, IReadOnlyList<RuntimeObjectMesh> surfaces)
    {
        if (cls.OClass != RedPlantClassId)
            return null;

        if (cls.JointCount != 13 || cls.Mesh.Indices.Length / 3 != 768)
            throw new InvalidDataException("R&C1 red-plant model no longer matches the retail witness.");
        var rest = cls.Sequences.Single(slot => slot.Index == RestSequenceId).Value
            ?? throw new InvalidDataException("R&C1 red-plant rest sequence is absent.");
        var reaction = cls.Sequences.Single(slot => slot.Index == ReactionSequenceId).Value
            ?? throw new InvalidDataException("R&C1 red-plant reaction sequence is absent.");

        if (rest.Frames.Count != 1 ||
            !Rac1MobyPose.CanPoseRigidHierarchy(cls.Mesh, cls.Joints, rest.Frames[0]) ||
            !Rac1MobyPose.IsRigidHierarchyRestAnchor(cls.Joints, rest.Frames[0]))
            throw new InvalidDataException("R&C1 red-plant rest anchor drifted outside the proven rigid subset.");
        if (reaction.Frames.Count != 20 || reaction.ConstantTransitionRateRaw != 0x3f000000u ||
            reaction.ConstantTransitionRate != 0.5f ||
            reaction.Frames.Any(frame => !Rac1MobyPose.CanPoseRigidHierarchy(cls.Mesh, cls.Joints, frame)))
            throw new InvalidDataException("R&C1 red-plant reaction clip no longer matches the retail witness.");

        var posedFrames = reaction.Frames
            .Select(frame => Rac1MobyPose.PoseRigidHierarchy(cls.Mesh, cls.Joints, frame))
            .ToArray();
        var animatedSurfaces = new List<RuntimeObjectAnimationSurface>(surfaces.Count);
        for (int surfaceIndex = 0; surfaceIndex < surfaces.Count; surfaceIndex++)
        {
            var surface = surfaces[surfaceIndex];
            var sourceVertices = new List<int>();
            var seen = new HashSet<int>();
            for (int face = 0; face < cls.TriangleTextureIds.Length; face++)
            {
                if (cls.TriangleTextureIds[face] != surface.TextureId)
                    continue;
                for (int corner = 0; corner < 3; corner++)
                {
                    int sourceVertex = cls.Mesh.Indices[face * 3 + corner];
                    if (seen.Add(sourceVertex)) sourceVertices.Add(sourceVertex);
                }
            }
            if (sourceVertices.Count * 3 != surface.Positions.Length)
                throw new InvalidDataException("R&C1 red-plant surface remap drifted from the dynamic model.");

            var frames = new List<double[]>(posedFrames.Length);
            foreach (var posed in posedFrames)
            {
                var local = new double[sourceVertices.Count * 3];
                for (int outputVertex = 0; outputVertex < sourceVertices.Count; outputVertex++)
                {
                    int source = sourceVertices[outputVertex] * 3;
                    int target = outputVertex * 3;
                    local[target] = posed[source];
                    local[target + 1] = posed[source + 2];
                    local[target + 2] = posed[source + 1];
                }
                frames.Add(local);
            }
            animatedSurfaces.Add(new RuntimeObjectAnimationSurface(surfaceIndex, frames));
        }

        double secondsPerFrame = 1d / (reaction.ConstantTransitionRate * NtscUpdateHz);
        var durations = Enumerable.Repeat(secondsPerFrame, reaction.Frames.Count).ToArray();
        return new RuntimeObjectAnimationSet([
            new RuntimeObjectAnimationClip(
                "reaction", RuntimeObjectAnimationRole.Reaction, animatedSurfaces, durations)
        ]);
    }
}
