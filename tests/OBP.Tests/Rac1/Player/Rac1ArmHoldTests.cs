using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Player;
using Xunit;

namespace OBP.Tests;

/// <summary>
/// The held-weapon arm layers: moby classes 1 and 2 supply the right and left arm "holding a gun"
/// versions of Ratchet's sequences 0..22, and 0x20dd20 keeps them on while a gun-type item is held.
/// </summary>
public sealed class Rac1ArmHoldTests
{
    [Fact]
    public void ArmSlotCountsFollowTheItemTable()
    {
        Assert.Equal(2, Rac1ArsenalRules.ArmSlotCount(Rac1WeaponId.Pyrocitor));
        Assert.Equal(2, Rac1ArsenalRules.ArmSlotCount(Rac1WeaponId.SuckCannon));
        Assert.Equal(2, Rac1ArsenalRules.ArmSlotCount(Rac1WeaponId.Devastator));
        Assert.Equal(2, Rac1ArsenalRules.ArmSlotCount(Rac1WeaponId.VisibombGun));
        Assert.Equal(2, Rac1ArsenalRules.ArmSlotCount(Rac1WeaponId.Ryno));
        Assert.Equal(1, Rac1ArsenalRules.ArmSlotCount(Rac1WeaponId.TeslaClaw));
        Assert.Equal(1, Rac1ArsenalRules.ArmSlotCount(Rac1WeaponId.MorphORay));
        Assert.Equal(1, Rac1ArsenalRules.ArmSlotCount(Rac1WeaponId.Blaster));
        Assert.Equal(1, Rac1ArsenalRules.ArmSlotCount(Rac1WeaponId.Taunter));
        foreach (var glove in new[] { Rac1WeaponId.FirstRanged, Rac1WeaponId.MineGlove, Rac1WeaponId.GloveOfDoom,
            Rac1WeaponId.Walloper, Rac1WeaponId.Wrench })
            Assert.Equal(0, Rac1ArsenalRules.ArmSlotCount(glove));
    }

    private static Rac1PlayerAnimationPresentationController Controller(string iso)
    {
        var avatar = Rac1PlayerAvatarProvider.Instance.Load(iso, Rac1PlayerAvatarProvider.RatchetAvatarId);
        return Assert.IsType<Rac1PlayerAnimationPresentationController>(
            Rac1PlayerAvatarProvider.Instance.CreateAnimationController(avatar));
    }

    [SkippableFact]
    public void RetailArmClassesDecodeToBothArmsForEveryBodySequenceBelow23()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        var rig = Controller(iso!).EquipmentRig!;
        Assert.True(rig.HasArmHold, rig.ArmHoldStatus);
        var slots = rig.ArmHoldClips!;
        Assert.Equal(2, slots.Count);
        for (int slot = 0; slot < 2; slot++)
        {
            // Every decoded body sequence below 23 has an arm hold version (idle, run, stop, jump, crouch, ...).
            // A sequence whose frame count differs from the body clip (retail sequence 17: 27 vs 25) cannot follow its clock.
            Assert.All(slots[slot].Keys, sequence => Assert.True(sequence < 23 && rig.Asset.Clips.ContainsKey(sequence)));
            Assert.True(slots[slot].Count >= 15, $"only {slots[slot].Count} aligned hold sequences");
            Assert.All(new[] { 0, 3, 4, 5, 6, 7, 8, 13 }, sequence => Assert.Contains(sequence, slots[slot].Keys));
            foreach (var (sequence, clip) in slots[slot])
            {
                Assert.Equal(20, clip.JointIndices.Count);
                Assert.Equal(rig.Asset.Clips[sequence].Frames.Count, clip.Frames.Count);
            }
        }
        // Right arm is the 52..71 bank and the left arm 72..91, as the slot joint lists 12 and 13.
        Assert.Equal(Enumerable.Range(52, 20), slots[0][0].JointIndices);
        Assert.Equal(Enumerable.Range(72, 20), slots[1][0].JointIndices);
    }

    [SkippableFact]
    public void ArmHoldWeightsRampByOneTenthPerUpdateAndFollowTheSlotCount()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        var controller = Controller(iso!);
        Assert.Equal((0f, 0f), controller.ArmHoldWeights);
        for (int i = 0; i < 4; i++) controller.StepArmHold(1);
        Assert.Equal(0.4f, controller.ArmHoldWeights.Right, 5);
        Assert.Equal(0f, controller.ArmHoldWeights.Left);
        for (int i = 0; i < 20; i++) controller.StepArmHold(2);
        Assert.Equal((1f, 1f), controller.ArmHoldWeights);
        for (int i = 0; i < 20; i++) controller.StepArmHold(0);
        Assert.Equal((0f, 0f), controller.ArmHoldWeights);
    }

    [SkippableFact]
    public void HeldGunMovesBothHandsAwayFromTheRelaxedIdleArmsAndCarriesTheWeaponSocket()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        var plain = Controller(iso!);
        plain.ApplyEquipmentPose(null);
        var plainHand = plain.CurrentAttachmentPose(Rac1WeaponProfiles.HandSocket(0));
        var plainLeft = plain.CurrentAttachmentPose(Rac1WeaponProfiles.HandSocket(1));

        var held = Controller(iso!);
        for (int i = 0; i < 12; i++) held.StepArmHold(2);
        var heldPose = held.ApplyEquipmentPose(null).SampledPose!.LocalPositions;
        var heldHand = held.CurrentAttachmentPose(Rac1WeaponProfiles.HandSocket(0));
        var heldLeft = held.CurrentAttachmentPose(Rac1WeaponProfiles.HandSocket(1));

        Assert.NotNull(heldHand);
        Assert.NotNull(plainHand);
        // The idle hand hangs at the side; the hold raises it and brings it forward.
        double moved = Math.Sqrt(Math.Pow(heldHand!.Origin.X - plainHand!.Origin.X, 2) +
            Math.Pow(heldHand.Origin.Y - plainHand.Origin.Y, 2) + Math.Pow(heldHand.Origin.Z - plainHand.Origin.Z, 2));
        Assert.True(moved > 0.1, $"the held right hand only moved {moved:0.000}");
        // Both hands are held: the left (slot 1) moves away from its relaxed position too.
        Assert.NotNull(heldLeft);
        Assert.NotNull(plainLeft);
        double movedLeft = Math.Sqrt(Math.Pow(heldLeft!.Origin.X - plainLeft!.Origin.X, 2) +
            Math.Pow(heldLeft.Origin.Y - plainLeft.Origin.Y, 2) + Math.Pow(heldLeft.Origin.Z - plainLeft.Origin.Z, 2));
        Assert.True(movedLeft > 0.05, $"the held left hand only moved {movedLeft:0.000}");
        // Skinned vertices changed.
        Assert.NotEqual(heldPose, plain.Current.SampledPose?.LocalPositions);
    }

    [SkippableFact]
    public void ABodyTransitionCarriesTheHeldHandFromItsOldPoseToTheNewOne()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        Rac1RatchetEquipmentRig rig = Controller(iso!).EquipmentRig!;
        rig.SetArmHoldWeights(1, 1);
        var baseline = new double[rig.Asset.Skin!.Positions.Length];
        double[] Hand() { var h = rig.Attachment(56).Origin; return [h.X, h.Y, h.Z]; }
        static double Gap(double[] a, double[] b) => Math.Sqrt(a.Zip(b, (x, y) => (x - y) * (x - y)).Sum());

        var idle = new OBP.RAC1.Player.Rac1AnimationFrameClock.Sample(0, 0, 1, 0f, false);
        var idleBody = rig.BasePose(idle);
        rig.BeginArmHoldTick(idle); rig.SampleArmHold(idle, baseline, idleBody); var before = Hand();

        // The body resolves its own transition blend; the hold must ride on that pose, not on the target.
        double[] Step(float fraction)
        {
            var sample = new OBP.RAC1.Player.Rac1AnimationFrameClock.Sample(7, 0, 1, fraction, true);
            rig.BeginArmHoldTick(sample); rig.SampleArmHold(sample, baseline, rig.BasePose(sample, idleBody));
            return Hand();
        }
        var start = Step(0f);
        var middle = Step(.5f);
        var end = Step(1f);

        Assert.True(Gap(start, before) < 1e-3, $"the held hand jumped {Gap(start, before):0.000} at the start of a blend");
        Assert.True(Gap(end, before) > .02, "the jump hold must differ from the idle hold, or this test proves nothing");
        Assert.InRange(Gap(middle, before), 1e-4, Gap(end, before));
    }

    [SkippableFact]
    public void ProceduralDeformationWitnessIsIndependentOfThePresentedVertices()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        var rig = Controller(iso!).EquipmentRig!;
        var basis = new Rac1AnimationFrameClock.Sample(0, 0, 1, 0, false);
        var body = rig.BasePose(basis);
        var offsets = new[] { new Rac1RatchetJointEases.Offset(rig.Asset.Skeleton.Attachments.Keys.First(),
            System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, .2f)) };
        var current = Enumerable.Repeat(12d, rig.Asset.Skin!.Positions.Length).ToArray();
        var result = rig.ApplyJointOffsets(basis, current, offsets, [], body).Positions;
        var witness = rig.JointEaseSkinChange!.Value;
        Assert.Equal(current.Length, witness.Before.Count);
        Assert.Equal(current.Length, witness.After.Count);
        var delta = witness.After.Zip(witness.Before, (after, before) => after - before).ToArray();
        Assert.Contains(delta, value => Math.Abs(value) > .00001);
        for (int i = 0; i < current.Length; i++) Assert.Equal(current[i] + delta[i], result[i]);
        // An arbitrary presentation change cannot be folded into the independent witness.
        result[0] += 1;
        Assert.NotEqual(result[0], current[0] + witness.After[0] - witness.Before[0]);
        var other = rig.ApplyJointOffsets(basis, Enumerable.Repeat(100d, current.Length).ToArray(), offsets, [], body).Positions;
        var repeated = rig.JointEaseSkinChange!.Value;
        Assert.Equal(delta, repeated.After.Zip(repeated.Before, (after, before) => after - before));
        for (int i = 0; i < current.Length; i++) Assert.Equal(100 + delta[i], other[i]);
    }

    [SkippableFact]
    public void TheWholeBodyUsePoseFadesInsteadOfSwitchingTheLegsAndTorsoAtOnce()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        Rac1RatchetEquipmentRig rig = Controller(iso!).EquipmentRig!;
        var baseline = rig.Asset.Skin!.Positions;
        var idle = new OBP.RAC1.Player.Rac1AnimationFrameClock.Sample(0, 0, 1, 0f, false);
        var use = new OBP.RAC1.Player.Rac1AnimationFrameClock.Sample(51, 0, 1, 0f, false);
        double[] At(float blend) => rig.Sample(idle, use, 1f, true, baseline, blend);
        var arms = At(0f); var half = At(.5f); var full = At(1f);
        // Pick the vertex the whole-body pose moves most (a leg or the torso, not an arm).
        int worst = Enumerable.Range(0, arms.Length).MaxBy(i => Math.Abs(full[i] - arms[i]));
        double total = Math.Abs(full[worst] - arms[worst]);
        Assert.True(total > .05, "the whole-body use pose must move the body, or this proves nothing");
        double moved = Math.Abs(half[worst] - arms[worst]);
        Assert.InRange(moved, total * .3, total * .7);
        // The graded call with a full blend equals the old all-or-nothing whole-body result.
        Assert.Equal(rig.Sample(idle, use, 1f, true, baseline), full);
    }

    [SkippableFact]
    public void NoHoldClipsExistForBodySequencesAbove22()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        var rig = Controller(iso!).EquipmentRig!;
        // No hold clips exist for body sequences the arm classes do not carry (flips, wrench, ...).
        foreach (var slot in rig.ArmHoldClips!)
            Assert.DoesNotContain(slot.Keys, sequence => sequence >= 23);
    }
}
