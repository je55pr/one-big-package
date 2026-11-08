using System.Numerics;
using OBP.PS2.Geometry;
using OBP.RAC1.Geometry;
using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OBP.RAC1.Gameplay;

/// <summary>The held-glove pose-copy path in 0x20f580, over its own retail skin and bind records.</summary>
public sealed record Rac1GloveSkin(PlayerSkeleton Skeleton, PlayerSkinnedMesh Skin, float CoordinateScale,
    int SocketJoint, Vector3 PayloadOffset)
{
    public sealed record Sample(double[] Positions, PlayerAvatarAttachmentPose PayloadPose);

    public static Rac1GloveSkin Decode(byte[] payload, Rac1Moby.Mesh mesh, int socketJoint, Vector3 offset)
    {
        var native = Ps2Skeleton.Read(payload);
        if (native.Joints.Count != 17) throw new InvalidDataException("A copied R&C1 glove pose requires 17 joints.");
        return new(new(native.Joints.Select(j => new PlayerSkeletonJoint(j.Parent, j.Translation, j.InverseBind)).ToArray(),
                native.Chains.Select((c, i) => (c, i)).ToDictionary(p => p.i, p => new PlayerSkeletonAttachment(p.c.Target, p.c.LayerJoints))),
            new(mesh.Positions, mesh.VertexJoints, mesh.VertexWeights), native.CoordinateScale, socketJoint, offset);
    }

    public Sample Pose(PlayerSkeletalSampler sampler, Rac1RatchetHandPose hand)
    {
        if (hand.Joints.Count + 1 != Skeleton.Joints.Count)
            throw new InvalidDataException("R&C1 hand pose does not fit the held glove.");
        sampler.SetLocal(0, new(Quaternion.Identity, Vector3.One, Skeleton.Joints[0].Translation));
        static float Quantize(float value, float scale) => unchecked((short)(int)MathF.Truncate(value * scale)) / scale;
        static Vector3 Quantized(Vector3 value, float scale) => new(Quantize(value.X, scale), Quantize(value.Y, scale), Quantize(value.Z, scale));
        for (int i = 0; i < hand.Joints.Count; i++)
        {
            var source = hand.Joints[i];
            var q = source.Rotation;
            var rawTranslation = Quantized(source.Translation!.Value / hand.CoordinateScale, 1);
            var bind = Skeleton.Joints[i + 1].Translation;
            // Encoding omits a translation record when its packed value agrees with the glove bind.
            var translation = rawTranslation == Quantized(bind / CoordinateScale, 1) ? bind : rawTranslation * CoordinateScale;
            sampler.SetLocal(i + 1, new(new(Quantize(q.X, 32768), Quantize(q.Y, 32768), Quantize(q.Z, 32768), Quantize(q.W, 32768)),
                Quantized(source.Scale!.Value, 4096), translation));
        }
        var positions = sampler.Skin();
        var socket = sampler.Attachment(SocketJoint);
        return new(positions, socket with { Origin = socket.Transform(new(PayloadOffset.X, PayloadOffset.Y, PayloadOffset.Z)) });
    }
}
