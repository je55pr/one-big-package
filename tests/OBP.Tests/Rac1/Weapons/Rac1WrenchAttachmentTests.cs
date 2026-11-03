using System.Buffers.Binary;
using OBP.RAC1.Animation;
using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class Rac1WrenchAttachmentTests
{
    [Fact]
    public void SemanticJointReadsLastChainByteAndRejectsTruncatedOrCrossClassMetadata()
    {
        byte[] bytes = new byte[0x90];
        bytes[8] = 3;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x1c), 0x60);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x60), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x64), 0x70);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x70), 3);
        bytes[0x74] = 0; bytes[0x75] = 1; bytes[0x76] = 2;
        Assert.Equal(2, Rac1WrenchAttachment.ReadJoint(bytes, 0));
        Assert.Throws<InvalidDataException>(() => Rac1WrenchAttachment.ReadJoint(bytes, 1));
        Assert.Throws<InvalidDataException>(() => Rac1WrenchAttachment.ReadJoint(bytes.AsSpan(0, 0x76), 0));
        bytes[0x75] = 3;
        Assert.Throws<InvalidDataException>(() => Rac1WrenchAttachment.ReadJoint(bytes, 0));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x1c), int.MaxValue);
        Assert.Throws<InvalidDataException>(() => Rac1WrenchAttachment.ReadJoint(bytes, 0));
    }

    [Fact]
    public void NativeRowMatrixMapsAttachmentPointWithoutDiscardingRotationScaleOrTranslation()
    {
        var hand = Rac1WrenchAttachment.ToAttachment(new Rac1MobyPose.JointPose(
            [10, 20, 30], new double[,] { { 0, 2, 0 }, { -3, 0, 0 }, { 0, 0, 4 } }));
        Assert.Equal(new PlayerAvatarPoint(4, 22, 42), hand.Transform(new(1, 2, 3)));
        var midway = PlayerAvatarAttachmentPose.Interpolate(hand,
            hand with { Origin = new(20, 30, 40) }, .5);
        Assert.Equal(new PlayerAvatarPoint(9, 27, 47), midway.Transform(new(1, 2, 3)));
    }

    [SkippableFact]
    public void RunStopAndFollowingIdleRetainTheMatchingAnimatedHandOnEveryUpdate()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        var avatar = Rac1PlayerAvatarProvider.Instance.Load(iso!, "ratchet");
        var controller = new Rac1PlayerAnimationPresentationController(avatar);
        for (int tick = 0; tick < 90; tick++)
            controller.StepOrdinaryAnimation(PlayerAnimationState.Run, tick / 60d,
                new(true, true, .095f, InputMagnitude: 1, ContactMotionLength: .095f, UncappedInputMagnitude: 1));
        bool sawStop = false, sawStanding = false;
        var idleHands = new HashSet<PlayerAvatarPoint>();
        for (int tick = 90; tick < 210; tick++)
        {
            float coast = Math.Max(0, .09f - (tick - 90) * .005f);
            controller.StepOrdinaryAnimation(PlayerAnimationState.Idle, tick / 60d,
                new(false, true, coast, ContactMotionLength: coast, UncappedInputMagnitude: 0));
            var hand = Assert.IsType<PlayerAvatarAttachmentPose>(
                controller.CurrentAttachmentPose(Rac1WrenchAttachment.SocketKey));
            if (controller.OrdinaryStopSample is not { } sample) continue;
            sawStop |= sample.Sequence is 5 or 6 or 20;
            if (sample.Sequence != 0 || sample.IsBlending) continue;
            sawStanding = true;
            idleHands.Add(hand.Origin);
            var track = avatar.AnimationClips.Single(clip => clip.Id == "standing")
                .AttachmentFrames![Rac1WrenchAttachment.SocketKey];
            Assert.Equal(PlayerAvatarAttachmentPose.Interpolate(track[sample.CurrentFrame],
                track[sample.NextFrame], sample.Fraction), hand);
        }
        Assert.True(sawStop && sawStanding);
        Assert.True(idleHands.Count > 10, "Hand attachment froze during the stop-owned standing animation.");
    }

    [SkippableFact]
    public void RetailAttackClipsCarryMovingHandAndOwnWrenchDeformation()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        var avatar = Rac1PlayerAvatarProvider.Instance.Load(iso!, "ratchet");
        foreach (string id in new[] { "standing", "locomotion-start", "sustained-locomotion", "primary-attack",
            "wrench-combo-second", "wrench-combo-third", "wrench-comet-strike", "wrench-hyper-strike" })
        {
            var clip = avatar.AnimationClips.Single(c => c.Id == id);
            var poses = clip.AttachmentFrames![Rac1WrenchAttachment.SocketKey];
            Assert.Equal(clip.FrameCount, poses.Count);
            Assert.True(poses.Select(pose => pose.Origin).Distinct().Count() > 1);
        }
        var neutral = avatar.AnimationClips.Single(c => c.Id == "standing").AttachmentFrames![Rac1WrenchAttachment.SocketKey][0];
        Assert.Equal(-.011197, neutral.Origin.X, 5);
        Assert.Equal(-.313031, neutral.Origin.Y, 5);
        Assert.Equal(.524381, neutral.Origin.Z, 5);
        var controller = new Rac1PlayerAnimationPresentationController(avatar);
        var first = new Rac1MovementAnimationSelection(0x13, 23, 1, 0, 5);
        for (int update = 0; update < 4; update++)
            controller.StepOrdinaryAnimation(PlayerAnimationState.Attack, update / 60d,
                new(false, true, 0, Traversal: first));
        var interrupted = controller.CurrentAttachmentPose(Rac1WrenchAttachment.SocketKey)!;
        var second = new Rac1MovementAnimationSelection(0x13, 24, 2, 1, 7);
        controller.StepOrdinaryAnimation(PlayerAnimationState.Attack, 4d / 60,
            new(false, true, 0, Traversal: second));
        Assert.Equal(interrupted, controller.CurrentAttachmentPose(Rac1WrenchAttachment.SocketKey));
        controller.StepOrdinaryAnimation(PlayerAnimationState.Attack, 5d / 60,
            new(false, true, 0, Traversal: second));
        var target = avatar.AnimationClips.Single(c => c.Id == "wrench-combo-second")
            .AttachmentFrames![Rac1WrenchAttachment.SocketKey][1];
        Assert.Equal(PlayerAvatarAttachmentPose.Interpolate(interrupted, target, 1f / 7),
            controller.CurrentAttachmentPose(Rac1WrenchAttachment.SocketKey));
        var wrench = Rac1WrenchAsset.Load(iso!);
        foreach (var profile in OBP.RAC1.Gameplay.Rac1WrenchAttackProfile.Profiles)
        {
            var clip = Rac1WrenchAsset.DecodeAnimation(wrench, profile.WrenchSequence);
            Assert.Equal(wrench.Sequences[profile.WrenchSequence].Value!.Frames.Count, clip.Frames.Count);
            Assert.Equal(clip.Frames.Count, clip.TipFrames.Count);
            Assert.All(clip.Frames, frame => Assert.All(frame, value => Assert.True(double.IsFinite(value))));
            if (clip.Frames.Count > 1)
                Assert.Contains(clip.Frames, frame => !frame.SequenceEqual(clip.Frames[0]));
        }
    }
}
