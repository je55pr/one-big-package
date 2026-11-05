using System.Numerics;
using OBP.Core;
using OBP.IO;
using OBP.RAC1.Level;
using OBP.RAC1.Player;
using OBP.Runtime.Player;
using Xunit;

namespace OBP.Tests.Rac1.Player;

/// <summary>
/// Ratchet's procedural joint layer (the native hero eases). The constants asserted
/// here come from the NTSC-U overlay table and the target rules recovered at 0x215340.
/// </summary>
public sealed class Rac1RatchetJointEasesTests
{
    // Attachment chains in native ease order, and their retail spring parameters.
    private static readonly int[] RetailAttachments = [9, 10, 11, 4, 22, 23, 7, 8, 13, 12, 6, 0, 21, 25, 26, 27, 28, 24];

    private static Rac1HeroEaseTable Table() => Rac1HeroEaseTable.Create(
        RetailAttachments.Select(id => new Rac1HeroEaseTable.Record(id, 0, 0)).ToArray());

    [Fact]
    public void ResetCopiesEachTableRecordsSpringParametersToItsOwnIndex()
    {
        // Distinct non-zero values per index, so a shifted or reversed copy cannot pass.
        var table = Rac1HeroEaseTable.Create(RetailAttachments
            .Select((id, i) => new Rac1HeroEaseTable.Record(id, 0.01f + i * 0.001f, 0.1f + i * 0.01f)).ToArray());
        var eases = new Rac1RatchetJointEases(table);
        eases.Reset();
        for (int i = 0; i < RetailAttachments.Length; i++)
            Assert.Equal((0.01f + i * 0.001f, 0.1f + i * 0.01f), eases.Parameters(i));
    }

    [Fact]
    public void EulerQuaternionUsesTheNativeNegatedAxisConventionAndXyzOrder()
    {
        var x = Rac1RatchetJointEases.EulerQuaternion(0.4f, 0, 0);
        Assert.Equal(-MathF.Sin(0.2f), x.X, 6);
        Assert.Equal(MathF.Cos(0.2f), x.W, 6);

        var combined = Rac1RatchetJointEases.EulerQuaternion(0.3f, 0.2f, 0.1f);
        var expected = Quaternion.Multiply(Quaternion.Multiply(
            Rac1RatchetJointEases.EulerQuaternion(0.3f, 0, 0),
            Rac1RatchetJointEases.EulerQuaternion(0, 0.2f, 0)),
            Rac1RatchetJointEases.EulerQuaternion(0, 0, 0.1f));
        Assert.True(Quaternion.Dot(combined, expected) > 0.999999f);
    }

    [Fact]
    public void SpringAngleFirstStepAndConvergenceMatchTheNativeRecurrence()
    {
        float angle = 0, velocity = 0;
        Rac1RatchetJointEases.SpringAngle(ref angle, ref velocity, 0.5f, 0.04f, 0.2f);
        Assert.Equal(0.02f, velocity, 6);
        Assert.Equal(0.02f, angle, 6);
        float peak = angle;
        for (int i = 0; i < 1200; i++)
        {
            Rac1RatchetJointEases.SpringAngle(ref angle, ref velocity, 0.5f, 0.04f, 0.2f);
            peak = MathF.Max(peak, angle);
        }
        // The native gain/damping pair is slightly underdamped: a small, bounded overshoot.
        Assert.InRange(peak, 0.5f, 0.5f * 1.3f);
        Assert.Equal(0.5f, angle, 3);
    }

    [Fact]
    public void SpringAngleCrossesZeroTheShortWayInsteadOfWrapping()
    {
        // Opposite signs force the sweep through zero rather than the short wrap.
        float angle = -3.0f, velocity = 0;
        Rac1RatchetJointEases.SpringAngle(ref angle, ref velocity, 3.0f, 0.04f, 0.2f);
        Assert.True(velocity > 0, "Direction rule 2 sweeps through zero.");
    }

    [Fact]
    public void OrdinaryRunTurnLeansTheSpineNeckAndCountersWithTheTail()
    {
        var eases = new Rac1RatchetJointEases(Table());
        eases.Step(new(Rac1EasePose.Run, YawError: 1.0f, Sequence: 4));
        // weight = clamp(1.0 * .5, +-.8), then weight * |weight| * 1.25.
        float weight = 0.5f * 0.5f * 1.25f;
        Assert.Equal(weight, eases.Target(Rac1RatchetJointEases.LowerSpine, 2), 6);
        Assert.Equal(weight, eases.Target(Rac1RatchetJointEases.Neck, 2), 6);
        Assert.Equal(weight * 0.25f, eases.Target(Rac1RatchetJointEases.Neck, 1), 6);
        Assert.Equal(-weight * 0.35f, eases.Target(Rac1RatchetJointEases.TailFirst, 2), 6);
        Assert.Equal(-weight * 0.3f, eases.Target(Rac1RatchetJointEases.TailLast, 2), 6);
        // The derived head ease follows the neck with its own clamp.
        Assert.Equal(weight * 0.25f, eases.Target(Rac1RatchetJointEases.HeadDerived, 2), 6);
        Assert.Equal((0.04f, 0.2f), eases.Parameters(Rac1RatchetJointEases.LowerSpine));
        Assert.Equal((0.02f, 0.2f), eases.Parameters(Rac1RatchetJointEases.Neck));
    }

    [Fact]
    public void LargeRunTurnsSaturateTheNativeWeight()
    {
        var eases = new Rac1RatchetJointEases(Table());
        eases.Step(new(Rac1EasePose.Run, YawError: 3.0f, Sequence: 4));
        Assert.Equal(0.8f * 0.8f * 1.25f, eases.Target(Rac1RatchetJointEases.Neck, 2), 6);
        eases.Step(new(Rac1EasePose.Run, YawError: -3.0f, Sequence: 4));
        Assert.Equal(-0.8f * 0.8f * 1.25f, eases.Target(Rac1RatchetJointEases.Neck, 2), 6);
    }

    [Fact]
    public void LandingAndAirborneUseTheirOwnSpringParametersAndTargets()
    {
        var eases = new Rac1RatchetJointEases(Table());
        eases.Step(new(Rac1EasePose.Land, YawError: 0.2f));
        Assert.Equal((0.008f, 0.08f), eases.Parameters(Rac1RatchetJointEases.Root));
        Assert.Equal(0.2f * 1.6f * 1.2f, eases.Target(Rac1RatchetJointEases.Neck, 2), 6);
        eases.Step(new(Rac1EasePose.Land, YawError: 1.0f));
        Assert.Equal(0.6f, eases.Target(Rac1RatchetJointEases.Neck, 2), 6); // native clamp
        eases.Step(new(Rac1EasePose.Airborne, YawError: 0.5f, Sequence: 7));
        Assert.Equal((0.04f, 0.2f), eases.Parameters(Rac1RatchetJointEases.Root));
        Assert.Equal(0.5f * 0.87f, eases.Target(Rac1RatchetJointEases.Neck, 2), 6);
        Assert.Equal(0.5f * -0.27f, eases.Target(Rac1RatchetJointEases.Chest, 0), 6);
    }

    [Fact]
    public void IdleHeadWanderStaysInsideTheNativeRangesAndMovesTheNeck()
    {
        var eases = new Rac1RatchetJointEases(Table());
        float widest = 0;
        for (int i = 0; i < 20000; i++)
        {
            eases.Step(new(Rac1EasePose.Idle, Sequence: 0));
            float yaw = eases.Target(Rac1RatchetJointEases.Neck, 2);
            Assert.InRange(MathF.Abs(yaw), 0, 0.99483764f + 1e-5f);
            widest = MathF.Max(widest, MathF.Abs(yaw));
            Assert.Equal(yaw * 0.52f, eases.Target(Rac1RatchetJointEases.LowerSpine, 2), 5);
        }
        Assert.True(widest > 0.349f, "A standing Ratchet must look around beyond the fidget clamp.");
    }

    [Theory]
    [InlineData(0.45f, -1.22173047f)]          // wall at the capsule surface: full raise, clamped
    [InlineData(1.00f, -0.88106f)]           // (1.3 - 0.55) / 1.3 * 1.25 * -1.2217
    [InlineData(1.75f, -0.52359879f)]          // wall at the probe end: the minimum raise
    public void HeldGunShoulderRisesAsAWallApproaches(float distance, float expected)
    {
        var eases = new Rac1RatchetJointEases(Table());
        eases.Step(new(Rac1EasePose.Idle, Sequence: 0, CarriesEquipment: true, ForwardWallDistance: distance));
        Assert.Equal(expected, eases.Target(Rac1RatchetJointEases.RightShoulder, 1), 4);
        Assert.Equal((0.05f, 0.3f), eases.Parameters(Rac1RatchetJointEases.RightShoulder));
    }

    [Fact]
    public void WallResponseNeedsAGunAndAWallAndLengthensTheNextProbeAfterAHit()
    {
        var eases = new Rac1RatchetJointEases(Table());
        Assert.Equal(1.3f, eases.WallProbeLength);
        eases.Step(new(Rac1EasePose.Idle, Sequence: 0, CarriesEquipment: false, ForwardWallDistance: 1.0f));
        Assert.Equal(0f, eases.Target(Rac1RatchetJointEases.RightShoulder, 1));
        eases.Step(new(Rac1EasePose.Idle, Sequence: 0, CarriesEquipment: true, ForwardWallDistance: null));
        Assert.Equal(0f, eases.Target(Rac1RatchetJointEases.RightShoulder, 1));
        Assert.Equal(1.3f, eases.WallProbeLength);
        eases.Step(new(Rac1EasePose.Idle, Sequence: 0, CarriesEquipment: true, ForwardWallDistance: 1.0f));
        Assert.Equal(1.6f, eases.WallProbeLength);
        eases.Step(new(Rac1EasePose.Idle, Sequence: 0, CarriesEquipment: true, ForwardWallDistance: null));
        Assert.Equal(1.3f, eases.WallProbeLength);
        Assert.Equal(0.57f, Rac1RatchetJointEases.WallProbeHeight(false));
        Assert.Equal(0.7f, Rac1RatchetJointEases.WallProbeHeight(true));
    }

    [Fact]
    public void TheShoulderOffsetReachesTheSolverAndRelaxesWhenTheWallIsGone()
    {
        var eases = new Rac1RatchetJointEases(Table());
        IReadOnlyList<Rac1RatchetJointEases.Offset> offsets = [];
        for (int i = 0; i < 90; i++)
            offsets = eases.Step(new(Rac1EasePose.None, CarriesEquipment: true, ForwardWallDistance: 0.6f));
        Assert.Contains(offsets, o => o.AttachmentId == 12); // ease 9 -> attachment chain 12 -> right shoulder joint 52
        Assert.True(eases.Angle(Rac1RatchetJointEases.RightShoulder, 1) < -0.5f);
        for (int i = 0; i < 2000; i++)
            offsets = eases.Step(new(Rac1EasePose.None, CarriesEquipment: true));
        Assert.DoesNotContain(offsets, o => o.AttachmentId == 12);
    }

    [Fact]
    public void TheHeadWaitsFiftyToOneHundredUpdatesAfterIdleEntryBeforeFirstLooking()
    {
        for (int seed = 1; seed <= 40; seed++)
        {
            var eases = new Rac1RatchetJointEases(Table(), seed);
            // Moving first, so the idle state is entered rather than already current.
            eases.Step(new(Rac1EasePose.Run, Sequence: 4));
            int firstLook = -1;
            for (int tick = 1; tick <= 400 && firstLook < 0; tick++)
            {
                eases.Step(new(Rac1EasePose.Idle, Sequence: 0));
                if (eases.Target(Rac1RatchetJointEases.Neck, 2) != 0) firstLook = tick;
            }
            // The entry roll is 50..100 and the look lands on the update the timer expires.
            Assert.InRange(firstLook, 50, 100);
        }
    }

    [Fact]
    public void ALevelStartHeroKeepsTheLongerInitialDelayBeforeFirstLooking()
    {
        for (int seed = 1; seed <= 40; seed++)
        {
            var eases = new Rac1RatchetJointEases(Table(), seed);
            int firstLook = -1;
            for (int tick = 1; tick <= 400 && firstLook < 0; tick++)
            {
                eases.Step(new(Rac1EasePose.Idle, Sequence: 0));
                if (eases.Target(Rac1RatchetJointEases.Neck, 2) != 0) firstLook = tick;
            }
            // The initializer rolls 180..300 and Reset keeps it through the first idle update.
            Assert.InRange(firstLook, 180, 300);
        }
    }

    [Fact]
    public void IdleEntryClearsTheStoredLookAndTailTargets()
    {
        var eases = new Rac1RatchetJointEases(Table());
        for (int i = 0; i < 400; i++) eases.Step(new(Rac1EasePose.Idle, Sequence: 0));
        Assert.NotEqual(0f, eases.Target(Rac1RatchetJointEases.Neck, 2));
        eases.Step(new(Rac1EasePose.Run, 0, 4));
        // Coming back to idle starts with a neutral head again, not the old look.
        eases.Step(new(Rac1EasePose.Idle, Sequence: 0));
        Assert.Equal(0f, eases.Target(Rac1RatchetJointEases.Neck, 2));
        Assert.Equal(0f, eases.Target(Rac1RatchetJointEases.Neck, 1));
    }

    [Fact]
    public void IdleFidgetSequencesKeepTheHeadTargetShort()
    {
        var eases = new Rac1RatchetJointEases(Table());
        for (int i = 0; i < 5000; i++)
        {
            eases.Step(new(Rac1EasePose.Idle, Sequence: i % 2 + 1));
            Assert.InRange(eases.Target(Rac1RatchetJointEases.Neck, 2), -0.34906584f - 1e-5f, 0.34906584f + 1e-5f);
            Assert.InRange(eases.Target(Rac1RatchetJointEases.Neck, 1), 0f, 0.2617994f + 1e-5f);
            Assert.InRange(eases.Target(Rac1RatchetJointEases.HeadDerived, 2), -0.034906585f - 1e-6f, 0.034906585f + 1e-6f);
        }
    }

    [Fact]
    public void TheTailOnlySwaysDuringTheNeutralStandingSequence()
    {
        var standing = new Rac1RatchetJointEases(Table());
        bool swayed = false;
        for (int i = 0; i < 2000; i++)
        {
            standing.Step(new(Rac1EasePose.Idle, Sequence: 0));
            swayed |= standing.Target(Rac1RatchetJointEases.TailFirst, 2) != 0;
        }
        Assert.True(swayed);

        var fidget = new Rac1RatchetJointEases(Table());
        for (int i = 0; i < 2000; i++)
        {
            fidget.Step(new(Rac1EasePose.Idle, Sequence: 1));
            Assert.Equal(0f, fidget.Target(Rac1RatchetJointEases.TailFirst, 2));
        }
    }

    [Fact]
    public void ReleasingTheStateRelaxesEveryJointAndDropsTheOffsets()
    {
        var eases = new Rac1RatchetJointEases(Table());
        for (int i = 0; i < 120; i++)
            Assert.NotEmpty(eases.Step(new(Rac1EasePose.Run, 1.2f, 4)));
        Assert.True(MathF.Abs(eases.Angle(Rac1RatchetJointEases.Neck, 2)) > 0.05f);

        IReadOnlyList<Rac1RatchetJointEases.Offset> last = [];
        for (int i = 0; i < 2000; i++) last = eases.Step(new(Rac1EasePose.None));
        Assert.Empty(last);
        for (int ease = 0; ease < Rac1RatchetJointEases.Count; ease++)
            for (int axis = 0; axis < 3; axis++)
                Assert.True(MathF.Abs(eases.Angle(ease, axis)) < 0.005f);
    }

    [Fact]
    public void OffsetsCarryTheTableAttachmentIdsAndUnitRotations()
    {
        var eases = new Rac1RatchetJointEases(Table());
        var offsets = eases.Step(new(Rac1EasePose.Run, 1.0f, 4));
        Assert.NotEmpty(offsets);
        foreach (var offset in offsets)
        {
            Assert.Contains(offset.AttachmentId, RetailAttachments);
            Assert.InRange(offset.Rotation.Length(), 0.9999f, 1.0001f);
        }
    }

    [Fact]
    public void ComposedLocalRotationMovesDescendantsThroughTheSamplerHierarchy()
    {
        var joints = new[]
        {
            new PlayerSkeletonJoint(-1, Vector3.Zero, Matrix4x4.Identity),
            new PlayerSkeletonJoint(0, new Vector3(0, 1, 0), Matrix4x4.Identity),
        };
        var skeleton = new PlayerSkeleton(joints, new Dictionary<int, PlayerSkeletonAttachment>());
        var channels = new[]
        {
            new PlayerJointChannels(Quaternion.Identity, Vector3.One, Vector3.Zero),
            new PlayerJointChannels(Quaternion.Identity, Vector3.One, new Vector3(0, 1, 0)),
        };
        var clip = new PlayerSkeletalClip(ObpSourceGame.Rac1, "test", 0, 0, [0, 1],
            [new PlayerSkeletalFrame(channels, 1, 0)]);
        var sampler = new PlayerSkeletalSampler(skeleton);
        sampler.Reset(clip, new(0, 0, 0));
        sampler.Skin();
        Assert.Equal(1f, sampler.JointMatrices[1].M42, 5);

        sampler.ComposeLocalRotation(0, Rac1RatchetJointEases.EulerQuaternion(0, 0, 0.3f));
        sampler.Skin();
        Assert.Equal(-MathF.Sin(0.3f), sampler.JointMatrices[1].M41, 5);
        Assert.Equal(MathF.Cos(0.3f), sampler.JointMatrices[1].M42, 5);
    }

    [SkippableFact]
    public void RetailTableMatchesTheRecoveredHeroEaseRecords()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var disc = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(disc).Levels.Single(l => l.LevelId == 0);
        var table = Rac1HeroEaseTable.Read(Rac1ExecutableOverlay.Read(disc, level));

        Assert.Equal(Rac1HeroEaseTable.BodyRecordCount, table.Body.Count);
        Assert.Equal(RetailAttachments, table.Body.Select(r => r.AttachmentId));
        // Spring parameters the table fixes for the eyes, head accessory and tail.
        Assert.Equal((0.12f, 0.3f), (table.Body[12].Acceleration, table.Body[12].MaxSpeed));
        Assert.Equal((0.015f, 0.3f), (table.Body[13].Acceleration, table.Body[13].MaxSpeed));
        Assert.Equal((0.015f, 0.3f), (table.Body[14].Acceleration, table.Body[14].MaxSpeed));
        Assert.Equal((0.013f, 0.25f), (table.Body[15].Acceleration, table.Body[15].MaxSpeed));
        Assert.Equal((0.017f, 0.21f), (table.Body[16].Acceleration, table.Body[16].MaxSpeed));
        Assert.Equal((0.03f, 0.2f), (table.Body[17].Acceleration, table.Body[17].MaxSpeed));
    }

    [SkippableFact]
    public void RetailEaseChainsResolveToTheRecoveredRatchetJoints()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        var avatar = Rac1PlayerAvatarProvider.Instance.Load(iso!, Rac1PlayerAvatarProvider.RatchetAvatarId);
        var skeleton = avatar.Skeletal!.Skeleton;
        // Root, lower spine, chest, neck, feet, hips, shoulders, hands, head accessory,
        // the four tail segments and the head, in native ease order.
        int[] expected = [0, 2, 6, 7, 100, 94, 98, 92, 72, 52, 54, 56, 18, 104, 106, 108, 110, 8];
        for (int i = 0; i < RetailAttachments.Length; i++)
            Assert.Equal(expected[i], skeleton.Attachments[RetailAttachments[i]].Joint);
    }
}
