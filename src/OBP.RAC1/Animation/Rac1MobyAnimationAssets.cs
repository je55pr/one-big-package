using System.Numerics;
using OBP.PS2.Presentation;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.RAC1.Animation;

/// <summary>Reusable class-local deformation, surface remapping, socket and sequence timing data.</summary>
public static class Rac1MobyAnimationAssets
{
    public sealed record Clip(int Sequence, string Id, float[] Rates, float[] Positions,
        int? DirectSound, IReadOnlyList<Rac1MobyAnimation.TimedSoundCue> Sounds, Vector3[] ContactSocket);
    public sealed record Asset(RuntimeObjectAnimationSet Presentation, IReadOnlyDictionary<int, Clip> Clips,
        float MinimumHeight, float MaximumHeight);

    public static Asset Bake(Rac1StaticClasses.MobyClass cls, IReadOnlyList<RuntimeObjectMesh> surfaces,
        ReadOnlySpan<byte> classPayload, bool skipUnsupportedTiming = false)
    {
        int joint;
        try { joint = Rac1MobyAttachment.ReadJoint(classPayload, 0); }
        catch (InvalidDataException) when (skipUnsupportedTiming && cls.Joints.Count > 0)
        {
            // LEVEL2 models may have no semantic socket0. Use the root joint for
            // animation contact presentation only, not native attack geometry.
            joint = 0;
        }
        var maps = new List<int[]>();
        foreach (var surface in surfaces)
        {
            var vertices = new List<int>(); var seen = new HashSet<int>();
            for (int face = 0; face < cls.TriangleTextureIds.Length; face++)
            {
                int state = cls.Mesh.TriangleMaterialStateIndices[face];
                var presentation = (uint)state < cls.Mesh.Materials.Count
                    ? Ps2MaterialPresentation.From(cls.Mesh.Materials[state], null, classifyMobySurface: true) : null;
                if (cls.TriangleTextureIds[face] != surface.TextureId || presentation != surface.MaterialPresentation) continue;
                for (int corner = 0; corner < 3; corner++)
                {
                    int vertex = cls.Mesh.Indices[face * 3 + corner];
                    if (seen.Add(vertex)) vertices.Add(vertex);
                }
            }
            if (vertices.Count * 3 != surface.Positions.Length)
                throw new InvalidDataException("R&C1 animated surface remap differs from authored geometry.");
            for (int at = 0; at < vertices.Count; at++)
            {
                int source = vertices[at] * 3;
                if (surface.Positions[at * 3] != cls.Mesh.Positions[source] ||
                    surface.Positions[at * 3 + 1] != cls.Mesh.Positions[source + 2] ||
                    surface.Positions[at * 3 + 2] != cls.Mesh.Positions[source + 1])
                    throw new InvalidDataException("R&C1 animation vertex order differs from its rendered surface.");
            }
            maps.Add(vertices.ToArray());
        }
        var clips = new Dictionary<int, Clip>(); var rendered = new List<RuntimeObjectAnimationClip>();
        float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
        foreach (var slot in cls.Sequences.Where(slot => slot.Value is not null))
        {
            var sequence = slot.Value!;
            var posed = sequence.Frames.Select(frame => Rac1MobyPose.PoseLocalHierarchy(cls.Mesh, cls.Joints, frame)).ToArray();
            var sockets = sequence.Frames.Select(frame =>
            {
                var point = Rac1MobyPose.PoseLocalJoints(cls.Mesh, cls.Joints, frame)[joint].Position;
                return new Vector3((float)point[0], (float)point[1], (float)point[2]);
            }).ToArray();
            var rates = sequence.Frames.Select(frame => sequence.Frames.Count == 1 ? 1f :
                sequence.ConstantTransitionRate > 0 ? sequence.ConstantTransitionRate : frame.TransitionRate).ToArray();
            if (rates.Any(rate => !float.IsFinite(rate) || rate <= 0 || rate > 1))
            {
                // Some LEVEL2 source clips encode a negative transition rate.
                // Its backwards-playback semantics are not yet reconstructed.
                // Omit only those unusable clips instead of inventing forward timing.
                if (skipUnsupportedTiming) continue;
                throw new InvalidDataException($"R&C1 class {cls.OClass} sequence {slot.Index} has an unsupported frame rate: " +
                    string.Join(",", rates.Where(rate => !float.IsFinite(rate) || rate <= 0 || rate > 1).Distinct()));
            }
            // Opaque clip labels are source-owned. The renderer never interprets a native sequence id.
            string name = "pose-" + (char)('a' + slot.Index);
            var animated = maps.Select((map, index) => new RuntimeObjectAnimationSurface(index, posed.Select(frame =>
            {
                var values = new double[map.Length * 3];
                for (int v = 0; v < map.Length; v++)
                {
                    values[v * 3] = frame[map[v] * 3];
                    values[v * 3 + 1] = frame[map[v] * 3 + 2];
                    values[v * 3 + 2] = frame[map[v] * 3 + 1];
                }
                return values;
            }).ToArray())).ToArray();
            rendered.Add(new(name, RuntimeObjectAnimationRole.Reaction, animated, rates.Select(rate => 1d / (60 * rate)).ToArray()));
            clips.Add(slot.Index, new(slot.Index, name, rates, sequence.Frames.Select(frame => frame.TimestampUnits / 16f).ToArray(),
                sequence.HasDirectSound ? sequence.SoundId : null, sequence.TimedSoundCues, sockets));
            if (slot.Index == 1)
                foreach (var frame in posed)
                    for (int v = 2; v < frame.Length; v += 3)
                    {
                        minimum = Math.Min(minimum, (float)frame[v]); maximum = Math.Max(maximum, (float)frame[v]);
                    }
        }
        return new(new(rendered), clips, minimum, maximum);
    }
}
