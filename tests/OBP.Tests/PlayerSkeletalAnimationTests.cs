using System.Numerics;
using OBP.Core;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class PlayerSkeletalAnimationTests
{
    private static PlayerSkeleton Rig() => new(
        [new(-1, Vector3.Zero, Matrix4x4.Identity), new(0, Vector3.UnitX, Matrix4x4.CreateTranslation(-Vector3.UnitX)),
            new(0, -Vector3.UnitX, Matrix4x4.CreateTranslation(Vector3.UnitX))],
        new Dictionary<int, PlayerSkeletonAttachment> { [0] = new(1, [1]), [1] = new(2, [2]) });

    private static PlayerSkeletalClip Clip(int sequence, int[] map, params PlayerJointChannels[] channels) => new(
        "test", "synthetic", 0, sequence, map, [new(channels, 1, 0)]);

    [Fact]
    public void JointAnimationTransfersBetweenDifferentVertexStreamsAndKeepsEachBind()
    {
        var clip = Clip(0, [0, 1, 2], new(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)),
            new(Quaternion.Identity), new(Quaternion.Identity));
        var first = new PlayerSkeletalSampler(Rig(), new([2, 0, 0], [1, 0, 0], [1, 0, 0]));
        var second = new PlayerSkeletalSampler(Rig(), new([3, 0, 0, -2, 0, 0], [1, 0, 0, 2, 0, 0], [1, 0, 0, 1, 0, 0]));
        first.Reset(clip, new(0, 0, 0)); second.Reset(clip, new(0, 0, 0));
        Near([0, -2, 0], first.Skin());
        Near([0, -3, 0, 0, 2, 0], second.Skin());
        Assert.Equal(first.Attachment(1), second.Attachment(1));
    }

    [Fact]
    public void ArmLayerPreservesUnderlyingTranslationScaleAndUnrelatedVertices()
    {
        var basis = Clip(0, [0, 1, 2], new(Quaternion.Identity, Vector3.One, new(0, 0, 1)),
            new(Quaternion.Identity), new(Quaternion.Identity));
        var arm = Clip(0, [1], new PlayerJointChannels(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)));
        var sampler = new PlayerSkeletalSampler(Rig(), new([2, 0, 0, -2, 0, 0], [1, 0, 0, 2, 0, 0], [1, 0, 0, 1, 0, 0]));
        sampler.Reset(basis, new(0, 0, 0)); sampler.Apply(arm, new(0, 0, 0), 1);
        double[] baseline = [20, 30, 40, -20, -30, -40];
        Near([1, -1, 1, -20, -30, -40], sampler.Skin(baseline, [1]));
        Assert.Equal(new PlayerAvatarPoint(1, 0, 1), sampler.Attachment(1).Origin);
    }

    [Fact]
    public void CompatibilityRejectsAChangedHandMapEvenWhenJointCountsMatch()
    {
        var rig = Rig(); var sampler = new PlayerSkeletalSampler(rig);
        var changed = rig with { Attachments = new Dictionary<int, PlayerSkeletonAttachment> { [0] = new(2, [2]), [1] = new(1, [1]) } };
        Assert.Throws<InvalidDataException>(() => sampler.RequireCompatible(changed));
        Assert.Throws<InvalidDataException>(() => new PlayerSkeletalSampler(rig with
        {
            Joints = [rig.Joints[0] with { Parent = 1 }, rig.Joints[1], rig.Joints[2]],
        }));
    }

    [Fact]
    public void TransitionBlendsJointsBeforeSkinningAndUsesTheSamePoseForTheHand()
    {
        var a = Clip(0, [0, 1, 2], new(Quaternion.Identity), new(Quaternion.Identity), new(Quaternion.Identity));
        var b = Clip(3, [0, 1, 2], new(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)),
            new(Quaternion.Identity), new(Quaternion.Identity));
        var sampler = new PlayerSkeletalSampler(Rig(), new([2, 0, 0], [1, 0, 0], [1, 0, 0]));
        var transition = new PlayerSkeletalTransition(); var key = new object();
        sampler.Reset(a, new(0, 0, 0)); transition.Resolve(sampler, null, 1);
        sampler.Reset(b, new(0, 0, 0)); transition.Resolve(sampler, key, 0);
        Near([2, 0, 0], sampler.Skin());
        sampler.Reset(b, new(0, 0, 0)); transition.Resolve(sampler, key, .5f);
        double unit = Math.Sqrt(.5);
        Near([2 * unit, -2 * unit, 0], sampler.Skin());
        var hand = sampler.Attachment(1);
        Near([unit, -unit, 0], [hand.Origin.X, hand.Origin.Y, hand.Origin.Z]);
        Assert.InRange(Math.Sqrt(hand.X.X * hand.X.X + hand.X.Y * hand.X.Y), .999999, 1.000001);
        sampler.Reset(b, new(0, 0, 0)); transition.Resolve(sampler, key, 1);
        Near([0, -2, 0], sampler.Skin());
    }

    [Fact]
    public void InterruptingATransitionFreezesTheCurrentlyEvaluatedPose()
    {
        var a = Clip(0, [0, 1, 2], new(Quaternion.Identity), new(Quaternion.Identity), new(Quaternion.Identity));
        var b = Clip(3, [0, 1, 2], new(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)),
            new(Quaternion.Identity), new(Quaternion.Identity));
        var sampler = new PlayerSkeletalSampler(Rig(), new([2, 0, 0], [1, 0, 0], [1, 0, 0]));
        var transition = new PlayerSkeletalTransition();
        sampler.Reset(a, new(0, 0, 0)); transition.Resolve(sampler, null, 1);
        sampler.Reset(b, new(0, 0, 0)); transition.Resolve(sampler, new object(), .4f);
        var interrupted = sampler.Skin(); var hand = sampler.Attachment(1);
        var restart = new object();
        sampler.Reset(a, new(0, 0, 0)); transition.Resolve(sampler, restart, 0);
        Near(interrupted, sampler.Skin());
        Assert.Equal(hand, sampler.Attachment(1));
        sampler.Reset(a, new(0, 0, 0)); transition.Resolve(sampler, restart, 1);
        Near([2, 0, 0], sampler.Skin());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void EquippedIdleUsesTheSelectedArmBanksWithoutRequestingAFullFiringPose(int arms)
    {
        var source = Source(arms); var player = new PlayerEquipmentAnimationPlayer();
        PlayerEquipmentPose? pose = null;
        for (int i = 0; i < 60; i++) pose = player.Step(source, 0, new(0, 0, 0), false, false, false, false);
        Assert.False(pose!.FullBody);
        Assert.Equal(arms, pose.Layers.Count);
        Assert.All(pose.Layers, layer => { Assert.Equal(0, layer.Clip.SequenceId); Assert.Equal(1, layer.Weight); });
        Assert.Null(player.CurrentUseSequence);
        Assert.Equal(0, player.UseWeight);
    }

    [Fact]
    public void UseThenReleaseReturnsToCarryAndSwitchingSourcesClearsTheOldUse()
    {
        var source = Source(1); var player = new PlayerEquipmentAnimationPlayer();
        var firing = player.Step(source, 0, new(0, 0, 0), false, false, true, true);
        Assert.True(firing.FullBody);
        Assert.Equal(5, player.CurrentUseSequence);
        for (int i = 0; i < 30; i++) player.Step(source, 0, new(0, 0, 0), false, false, false, false);
        Assert.Null(player.CurrentUseSequence);
        player.Step(source, 0, new(0, 0, 0), false, false, true, true);
        var switched = player.Step(Source(2), 0, new(0, 0, 0), false, false, false, false);
        Assert.False(switched.FullBody);
        Assert.Null(player.CurrentUseSequence);
        Assert.Equal(2, switched.Layers.Count);
    }

    private static PlayerEquipmentAnimationSource Source(int arms)
    {
        var body = new Dictionary<int, PlayerSkeletalClip>
        {
            [0] = Clip(0, [0, 1, 2], new(Quaternion.Identity), new(Quaternion.Identity), new(Quaternion.Identity)),
            [5] = Clip(5, [0, 1, 2], new(Quaternion.Identity), new(Quaternion.Identity), new(Quaternion.Identity)),
        };
        var avatar = new PlayerSkeletalAsset(new(ObpSourceGame.Rac1, "synthetic", "test", "test"), Rig(), null, body);
        return new(avatar, [new Dictionary<int, PlayerSkeletalClip> { [0] = Clip(0, [1], new PlayerJointChannels(Quaternion.Identity)) },
            new Dictionary<int, PlayerSkeletalClip> { [0] = Clip(0, [2], new PlayerJointChannels(Quaternion.Identity)) }], arms, 1, 5, -1, 5, true, 2)
        { ArmBankUpperExclusive = 23, BlendSteps = new(.25f, .07f, .25f) };
    }

    private static void Near(double[] expected, double[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++) Assert.InRange(Math.Abs(expected[i] - actual[i]), 0, .000002);
    }
}
