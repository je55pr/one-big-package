using System.Buffers.Binary;
using System.Numerics;
using OBP.Core;
using OBP.PS2.Animation;
using OBP.PS2.Geometry;
using OBP.RAC3.Level;
using OBP.Runtime.Player;

namespace OBP.RAC3.Player;

/// <summary>Veldin's original player and arm carriers; Cannon selectors at 0x4e6a08 and definition 43.</summary>
public static class Rac3RatchetSkeletalSource
{
    public static PlayerEquipmentAnimationSource ReadShockCannon(Rac3LevelCore.Core core)
    {
        byte[] bodyClass = Payload(0);
        var source = Ps2Skeleton.Read(bodyClass);
        if (source.Joints.Count != 111) throw new InvalidDataException("UYA player rig differs from the supported authority.");
        var skeleton = new PlayerSkeleton(source.Joints.Select(j => new PlayerSkeletonJoint(j.Parent, j.Translation, j.InverseBind)).ToArray(),
            source.Chains.Select((c, i) => (c, i)).ToDictionary(p => p.i, p => new PlayerSkeletonAttachment(p.c.Target, p.c.LayerJoints)));
        var body = new Dictionary<int, PlayerSkeletalClip>();
        int[] bodyMap = Enumerable.Range(0, source.Joints.Count).ToArray();
        for (int id = 0; id < 256; id++)
        {
            int at = I32(core.Index, core.Header.RatchetSeqsOffset + id * 4);
            if (at == 0) continue;
            int end = core.SectionBoundaries.FirstOrDefault(b => b > at, core.Assets.Length);
            if (at < 0 || at >= end || end > core.Assets.Length) throw new InvalidDataException("UYA player sequence escapes its asset section.");
            body.Add(id, Convert(Ps2SkeletalAnimation.ReadSequence(core.Assets.AsSpan(at, end - at), 0, 111, id, true), 0, bodyMap));
        }
        var arms = new List<IReadOnlyDictionary<int, PlayerSkeletalClip>>();
        for (int carrier = 1; carrier <= 2; carrier++)
        {
            byte[] bytes = Payload(carrier);
            int[] map = Ps2Skeleton.ReadAnimationJointMap(bytes, 111);
            if (!map.SequenceEqual(skeleton.Attachments[11 + carrier].LayerJoints))
                throw new InvalidDataException("UYA arm channels disagree with the native layer list.");
            var clips = new Dictionary<int, PlayerSkeletalClip>();
            for (int id = 0; id < bytes[0xc]; id++)
            {
                int at = I32(bytes, 0x48 + id * 4);
                if (at != 0) clips.Add(id, Convert(Ps2SkeletalAnimation.ReadSequence(bytes, at, map.Length, id, false, 111), carrier, map));
            }
            arms.Add(clips);
        }
        var mesh = GcUyaMoby.ReadClass(bodyClass);
        var avatar = new PlayerSkeletalAsset(new(ObpSourceGame.Rac3, Rac3Authority.Primary.BuildId, "ratchet", "rac3:ratchet:moby-class-0"),
            skeleton, mesh.Positions.Length == 0 ? null : new(mesh.Positions, mesh.VertexJoints, mesh.VertexWeights), body);
        // Native row43: hand matrix0, both arms, full49 / moving-1 / crouch65,
        // one-cycle ownership; the Cannon entry blend is two updates.
        return new(avatar, arms, 2, skeleton.Attachments[0].Joint, 49, -1, 65, false, 2)
        { BlendSteps = new(.25f, .07f, .25f), ArmBankUpperExclusive = 23 };

        byte[] Payload(int id)
        {
            var table = core.Header.MobyClasses;
            for (int i = 0; i < table.Count; i++)
            {
                int row = table.Offset + i * 0x20;
                if (I32(core.Index, row + 4) != id) continue;
                int at = I32(core.Index, row), end = core.SectionBoundaries.FirstOrDefault(b => b > at, core.Assets.Length);
                if (at <= 0 || at >= end || end > core.Assets.Length) throw new InvalidDataException("Invalid UYA player carrier.");
                return core.Assets[at..end];
            }
            throw new InvalidDataException($"UYA player carrier {id} is absent.");
        }

        PlayerSkeletalClip Convert(Ps2SkeletalAnimation.Sequence clip, int carrier, int[] map)
        {
            var frames = clip.Frames.Select(frame =>
            {
                float rate = clip.ConstantTransitionRate > 0 ? clip.ConstantTransitionRate : frame.TransitionRate;
                if (rate <= 0 && clip.Frames.Count != 1) throw new InvalidDataException("Unsupported stopped UYA player clip.");
                // Native layer baking starts with the target skeleton's
                // common translations and unit scales, then applies sparse
                // auxiliary vectors. It does not inherit animated body offsets.
                var channels = map.Select((j, i) => new PlayerJointChannels(frame.Rotations[i].Quaternion,
                    Vector3.One, skeleton.Joints[j].Translation)).ToArray();
                foreach (var v in frame.Scales) { int j = Array.IndexOf(map, v.Joint); if (j < 0) throw new InvalidDataException("UYA scale escapes its layer."); channels[j] = channels[j] with { Scale = v.Vector / 4096 }; }
                foreach (var v in frame.Translations) { int j = Array.IndexOf(map, v.Joint); if (j < 0) throw new InvalidDataException("UYA translation escapes its layer."); channels[j] = channels[j] with { Translation = v.Vector * source.CoordinateScale }; }
                return new PlayerSkeletalFrame(channels, rate > 0 ? 1d / (rate * 60) : 1d / 60, frame.TimelineUnits / 16d);
            }).ToArray();
            return new(ObpSourceGame.Rac3, Rac3Authority.Primary.BuildId, carrier, clip.Id, map, frames);
        }
    }

    private static int I32(byte[] b, int at)
    {
        if (at < 0 || at > b.Length - 4) throw new InvalidDataException("UYA skeletal directory exceeds its source.");
        return BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
    }
}
