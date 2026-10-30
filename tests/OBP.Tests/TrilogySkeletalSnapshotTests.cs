using System.Numerics;
using System.Security.Cryptography;
using OBP.Core;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed partial class TrilogySkeletalRetailTests
{
    // Exact post-conversion output golden data, recorded from the pre-refactor source readers.
    // These tests are source-gated: never commit retail payloads or reconstructed frames.
    [SkippableTheory]
    [InlineData(ObpSourceGame.Rac2, "4d423655da2f2dc5a9f427e16360bed333fb150430d0c56b3129fb370fe0465e")]
    [InlineData(ObpSourceGame.Rac3, "d003243475bf1b8b5478f8aa673b98798c169f007bebd7119944e7964de3db60")]
    public void SkeletalReaderOutputMatchesOriginalAdapter(ObpSourceGame game, string expected)
    {
        using var sources = new Sources();
        var source = game switch
        {
            ObpSourceGame.Rac2 => sources.Rac2,
            ObpSourceGame.Rac3 => sources.Rac3,
            _ => throw new ArgumentOutOfRangeException(nameof(game)),
        };
        string actual = Fingerprint(source);
        _output.WriteLine($"{game}: {actual}");
        Assert.Equal(expected, actual);
    }

    private static string Fingerprint(PlayerEquipmentAnimationSource source)
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            var avatar = source.Avatar;
            w.Write((int)avatar.Identity.SourceGame);
            w.Write(avatar.Identity.BuildId);
            w.Write(avatar.Identity.AvatarId);
            w.Write(avatar.Identity.ModelId);
            w.Write(avatar.Skeleton.Joints.Count);
            foreach (var joint in avatar.Skeleton.Joints)
            {
                w.Write(joint.Parent);
                Vector(joint.Translation);
                Matrix(joint.InverseBind);
            }
            w.Write(avatar.Skeleton.Attachments.Count);
            foreach (var (id, attachment) in avatar.Skeleton.Attachments.OrderBy(p => p.Key))
            {
                w.Write(id); w.Write(attachment.Joint);
                Integers(attachment.LayerJoints);
            }
            w.Write(avatar.Skin is not null);
            if (avatar.Skin is { } skin)
            {
                w.Write(skin.Positions.Length);
                foreach (double p in skin.Positions) w.Write(p);
                Integers(skin.Joints);
                w.Write(skin.Weights.Length);
                foreach (float weight in skin.Weights) w.Write(weight);
            }
            Clips(avatar.Clips);
            w.Write(source.Arms.Count);
            foreach (var bank in source.Arms) Clips(bank);
            w.Write(source.ArmCount);
            w.Write(source.HandJoint);
            w.Write(source.StandingUseSequence);
            w.Write(source.MovingUseSequence);
            w.Write(source.CrouchingUseSequence);
            w.Write(source.SustainUse);
            w.Write(source.UseBlendTicks);
            w.Write(source.ArmBankUpperExclusive);
            w.Write(source.BlendSteps.CarryIn);
            w.Write(source.BlendSteps.UseOut);
            w.Write(source.BlendSteps.CompletedUseOut);

            void Clips(IReadOnlyDictionary<int, PlayerSkeletalClip> clips)
            {
                w.Write(clips.Count);
                foreach (var (id, clip) in clips.OrderBy(p => p.Key))
                {
                    w.Write(id);
                    w.Write((int)clip.SourceGame);
                    w.Write(clip.BuildId);
                    w.Write(clip.CarrierId);
                    w.Write(clip.SequenceId);
                    Integers(clip.JointIndices);
                    w.Write(clip.Frames.Count);
                    foreach (var frame in clip.Frames)
                    {
                        w.Write(frame.DurationSeconds);
                        w.Write(frame.TimelinePosition);
                        w.Write(frame.Joints.Count);
                        foreach (var j in frame.Joints)
                        {
                            Quaternion(j.Rotation);
                            w.Write(j.Scale.HasValue);
                            if (j.Scale is { } scale) Vector(scale);
                            w.Write(j.Translation.HasValue);
                            if (j.Translation is { } translation) Vector(translation);
                        }
                    }
                }
            }
            void Integers(IReadOnlyList<int> xs) { w.Write(xs.Count); foreach (int n in xs) w.Write(n); }
            void Vector(Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
            void Quaternion(Quaternion q) { w.Write(q.X); w.Write(q.Y); w.Write(q.Z); w.Write(q.W); }
            void Matrix(Matrix4x4 m)
            {
                w.Write(m.M11); w.Write(m.M12); w.Write(m.M13); w.Write(m.M14);
                w.Write(m.M21); w.Write(m.M22); w.Write(m.M23); w.Write(m.M24);
                w.Write(m.M31); w.Write(m.M32); w.Write(m.M33); w.Write(m.M34);
                w.Write(m.M41); w.Write(m.M42); w.Write(m.M43); w.Write(m.M44);
            }
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }
}
