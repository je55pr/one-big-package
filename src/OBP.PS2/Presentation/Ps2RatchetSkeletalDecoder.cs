using System.Buffers.Binary;
using System.Numerics;
using OBP.Core;
using OBP.PS2.Animation;
using OBP.PS2.Geometry;
using OBP.Runtime.Player;

namespace OBP.PS2.Presentation;

/// <summary>
/// Mechanics shared by the *supported* GC and UYA Ratchet class-0 skeletal carriers.
/// Source discovery, build admission, asset identity and equipment policy stay with
/// their respective game adapters. This does not imply generic PS2 rig compatibility.
/// </summary>
public static class Ps2RatchetSkeletalDecoder
{
    public sealed record Decoded(
        byte[] BodyClass,
        PlayerSkeleton Skeleton,
        IReadOnlyDictionary<int, PlayerSkeletalClip> Body,
        IReadOnlyList<IReadOnlyDictionary<int, PlayerSkeletalClip>> Arms);

    /// <param name="loadCarrier">Game-specific native class lookup and validation.</param>
    /// <param name="diagnosticName">Native diagnostic prefix (GC or UYA).</param>
    public static Decoded Read(
        byte[] index, byte[] assets, IReadOnlyList<int> sectionBoundaries, int sequencesOffset,
        Func<int, byte[]> loadCarrier, ObpSourceGame sourceGame, string buildId, string diagnosticName)
    {
        byte[] bodyClass = loadCarrier(0);
        var source = Ps2Skeleton.Read(bodyClass);
        if (source.Joints.Count != 111)
            throw new InvalidDataException($"{diagnosticName} player rig differs from the supported authority.");
        var skeleton = new PlayerSkeleton(
            source.Joints.Select(j => new PlayerSkeletonJoint(j.Parent, j.Translation, j.InverseBind)).ToArray(),
            source.Chains.Select((c, i) => (c, i)).ToDictionary(p => p.i,
                p => new PlayerSkeletonAttachment(p.c.Target, p.c.LayerJoints)));

        var body = new Dictionary<int, PlayerSkeletalClip>();
        int[] bodyMap = Enumerable.Range(0, source.Joints.Count).ToArray();
        for (int id = 0; id < 256; id++)
        {
            int at = I32(index, sequencesOffset + id * 4);
            if (at == 0) continue;
            int end = sectionBoundaries.FirstOrDefault(b => b > at, assets.Length);
            if (at < 0 || at >= end || end > assets.Length)
                throw new InvalidDataException($"{diagnosticName} player sequence escapes its asset section.");
            body.Add(id, Convert(Ps2SkeletalAnimation.ReadSequence(assets.AsSpan(at, end - at), 0, 111, id, true),
                0, bodyMap));
        }

        var arms = new List<IReadOnlyDictionary<int, PlayerSkeletalClip>>();
        for (int carrier = 1; carrier <= 2; carrier++)
        {
            byte[] bytes = loadCarrier(carrier);
            int[] map = Ps2Skeleton.ReadAnimationJointMap(bytes, 111);
            if (!skeleton.Attachments.TryGetValue(11 + carrier, out var attachment))
                throw new InvalidDataException($"{diagnosticName} arm attachment {11 + carrier} is missing.");
            if (!map.SequenceEqual(attachment.LayerJoints))
                throw new InvalidDataException($"{diagnosticName} arm channels disagree with the native layer list.");
            var clips = new Dictionary<int, PlayerSkeletalClip>();
            for (int id = 0; id < bytes[0xc]; id++)
            {
                int at = I32(bytes, 0x48 + id * 4);
                if (at != 0)
                    clips.Add(id, Convert(Ps2SkeletalAnimation.ReadSequence(bytes, at, map.Length, id, false, 111),
                        carrier, map));
            }
            arms.Add(clips);
        }
        return new(bodyClass, skeleton, body, arms);

        PlayerSkeletalClip Convert(Ps2SkeletalAnimation.Sequence clip, int carrier, int[] map)
        {
            var frames = clip.Frames.Select(frame =>
            {
                float rate = clip.ConstantTransitionRate > 0 ? clip.ConstantTransitionRate : frame.TransitionRate;
                if (rate <= 0 && clip.Frames.Count != 1)
                    throw new InvalidDataException($"Unsupported stopped {diagnosticName} player clip.");
                // Each layer begins with native common translations and unit scales.
                // Sparse auxiliary vectors do not inherit animated body offsets.
                var channels = map.Select((j, i) => new PlayerJointChannels(frame.Rotations[i].Quaternion,
                    Vector3.One, skeleton.Joints[j].Translation)).ToArray();
                foreach (var v in frame.Scales)
                {
                    int j = Array.IndexOf(map, v.Joint);
                    if (j < 0) throw new InvalidDataException($"{diagnosticName} scale escapes its layer.");
                    channels[j] = channels[j] with { Scale = v.Vector / 4096 };
                }
                foreach (var v in frame.Translations)
                {
                    int j = Array.IndexOf(map, v.Joint);
                    if (j < 0) throw new InvalidDataException($"{diagnosticName} translation escapes its layer.");
                    channels[j] = channels[j] with { Translation = v.Vector * source.CoordinateScale };
                }
                return new PlayerSkeletalFrame(channels, rate > 0 ? 1d / (rate * 60) : 1d / 60,
                    frame.TimelineUnits / 16d);
            }).ToArray();
            return new(sourceGame, buildId, carrier, clip.Id, map, frames);
        }

        int I32(byte[] b, int at)
        {
            if (at < 0 || at > b.Length - 4)
                throw new InvalidDataException($"{diagnosticName} skeletal directory exceeds its source.");
            return BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
        }
    }
}
