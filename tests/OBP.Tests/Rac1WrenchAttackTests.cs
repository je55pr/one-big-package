using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1WrenchAttackTests
{
    private static Rac1WrenchAttackSession.Input Input(bool pressed = false, float time = 16) =>
        new(pressed, false, false, true, 0, 11, 0, false, 1, 0, 0, 0, time, false, false);

    [Fact]
    public void RepeatingSameThrowProfileIsANewActionEntryWithSameSequence()
    {
        var actions = new Rac1PlayerActionRuntimeSession();
        var first = actions.ApplyWrenchAttack(Rac1WrenchAttackProfile.Profiles[3]);
        actions.Update();
        var repeated = actions.ApplyWrenchAttack(Rac1WrenchAttackProfile.Profiles[3]);
        Assert.Equal(26, repeated.NativeSequence);
        Assert.Equal(0x15, repeated.PreviousNativeState);
        Assert.Equal(first.EntryGeneration + 1, repeated.EntryGeneration);
        Assert.Equal(0, repeated.UpdateGeneration);
    }

    [Fact]
    public void GroundedAttackRetainsNativeDownwardContactRequestAndAirborneHandoffDoesNotResetVelocity()
    {
        var attack = new Rac1WrenchAttackSession();
        Assert.Equal(-54f / 3600, attack.Step(Input(true)).Vertical, 7);
        var movement = new Rac1RatchetMovementController();
        movement.ResumeAfterExternalAction(.1, -.03, -.4, grounded: false);
        Assert.Equal(Rac1RatchetMovementPhase.Falling, movement.Phase);
        Assert.Equal(-.4, movement.VerticalStep);
        Assert.Equal(.1, movement.PlanarX);
        movement.ResumeAfterExternalAction(0, 0, -.4, grounded: true);
        Assert.Equal(Rac1RatchetMovementPhase.Grounded, movement.Phase);
        Assert.Equal(0, movement.VerticalStep);
    }

    [Fact]
    public void MultiStrikeUsesThreeDistinctBodyAndWrenchClipsAndRequiresNewPresses()
    {
        var attack = new Rac1WrenchAttackSession();
        var first = attack.Step(Input(true));
        Assert.Equal(23, first.Animation?.Sequence);
        Assert.False(first.HitActive);
        Assert.Equal(0, attack.Step(Input(time: 26)).ProfileId);
        var second = attack.Step(Input(true, 26));
        Assert.Equal(1, second.ProfileId);
        Assert.Equal(24, second.Animation?.Sequence);
        Assert.Equal(4, Rac1WrenchAttackProfile.Profiles[second.ProfileId].WrenchSequence);
        var third = attack.Step(Input(true, 12));
        Assert.Equal(2, third.ProfileId);
        Assert.Equal(25, third.Animation?.Sequence);
        Assert.Equal(5, Rac1WrenchAttackProfile.Profiles[third.ProfileId].WrenchSequence);
        Assert.True(third.Generation > second.Generation && second.Generation > first.Generation);
        Assert.False(attack.Step(Input(time: 22)).Active);
    }

    [Theory]
    [InlineData(16.99f, false, false)]
    [InlineData(17f, false, true)]
    [InlineData(23f, false, true)]
    [InlineData(23.01f, false, false)]
    [InlineData(20f, true, false)]
    public void FirstSwingContactUsesInclusiveAuthoredTimeAndNeverPresetBlend(float time, bool blending, bool hit)
    {
        var attack = new Rac1WrenchAttackSession();
        attack.Step(Input(true));
        Assert.Equal(hit, attack.Step(Input(time: time) with { AnimationBlending = blending }).HitActive);
    }

    [Fact]
    public void MotionGateUsesAuthoredTimeRatherThanEighteenElapsedUpdates()
    {
        var attack = new Rac1WrenchAttackSession();
        var entry = attack.Step(Input(true));
        Assert.Equal(37f / 3600, entry.PlanarX, 7);
        var endOfLunge = attack.Step(Input(time: 18));
        Assert.Equal((37f - 28) / 3600, endOfLunge.PlanarX, 7);
        Assert.Equal(0, attack.Step(Input(time: 18)).PlanarX);
        Assert.True(attack.Active);
    }

    [Fact]
    public void ChainPressIsBufferedButExpiredPressAndLateRecoveryCannotSelectSecondSwing()
    {
        var buffered = new Rac1WrenchAttackSession();
        buffered.Step(Input(true));
        buffered.Step(Input(true, 24));
        Assert.Equal(1, buffered.Step(Input(time: 26)).ProfileId);
        var expired = new Rac1WrenchAttackSession();
        expired.Step(Input(true));
        for (int i = 0; i < 20; i++) expired.Step(Input(time: 20));
        Assert.Equal(0, expired.Step(Input(time: 26)).ProfileId);
        Assert.False(expired.Step(Input(time: 32)).Active);
        Assert.Equal(0, expired.Step(Input(true, 32)).ProfileId);
    }

    [Theory]
    [InlineData(6, 11, 0, .11f, false, 4)]
    [InlineData(6, 10, 0, 1f, false, -1)]
    [InlineData(7, 11, 0, .56f, true, 0)]
    [InlineData(7, 11, 0, .58f, true, 4)]
    [InlineData(8, 11, 0, .56f, false, 0)]
    [InlineData(11, 11, 21, 1f, false, 4)]
    [InlineData(11, 11, 20, 1f, false, -1)]
    [InlineData(14, 11, 33, 1f, false, 4)]
    [InlineData(14, 11, 32, 1f, false, -1)]
    [InlineData(10, 30, 50, 1f, false, -1)]
    [InlineData(16, 30, 50, 1f, false, -1)]
    public void AirborneAdmissionRetainsNativeActionAgeApexAndClearanceGates(int action, int age,
        float animation, float height, bool apex, int expected)
    {
        var input = Input(true) with
        {
            Grounded = false,
            MovementAction = action,
            MovementAge = age,
            MovementAnimationTime = animation,
            GroundClearance = height,
            HasApexed = apex
        };
        Assert.Equal(expected < 0 ? (int?)null : expected, Rac1WrenchAttackSession.SelectAdmission(input));
    }

    [Fact]
    public void HyperStrikeRisesThenDivesAndKeepsItsTwoPointDamage()
    {
        var attack = new Rac1WrenchAttackSession();
        var input = Input(true) with { Grounded = false, MovementAction = 6 };
        var entry = attack.Step(input);
        Assert.Equal(0x14, entry.NativeAction);
        Assert.Equal(43, entry.Animation?.Sequence);
        Assert.Equal(2, entry.Damage);
        Assert.True(entry.Vertical > 0);
        Assert.Equal(.33f, entry.Animation?.PlaybackMultiplier);
        Rac1WrenchAttackSession.Frame frame = entry;
        for (int i = 0; i < 30; i++) frame = attack.Step(input with { Pressed = false, AnimationTime = 24 });
        Assert.True(frame.Vertical < 0);
        Assert.Equal(1f, frame.Animation?.PlaybackMultiplier);
        var landed = attack.Step(Input(time: 24));
        Assert.Equal(0, landed.Vertical);
        Assert.Equal(2f, landed.Animation?.PlaybackMultiplier);
        Assert.True(landed.HitActive);
    }

    [Fact]
    public void CometStrikeReleasesOnceAfterMarkerAndKeepsLoopUntilWrenchReturns()
    {
        var attack = new Rac1WrenchAttackSession();
        var entry = attack.Step(Input(true) with { CrouchHeld = true });
        Assert.Equal(0x15, entry.NativeAction);
        Assert.Equal(26, entry.Animation?.Sequence);
        Assert.Equal(6, entry.Animation?.LoopStartFrame);
        Assert.Equal(21, entry.Animation?.LoopEndFrame);
        Assert.False(attack.Step(Input(time: 33)).ReleaseWrench);
        Assert.False(attack.Step(Input(time: 34) with { AnimationBlending = true }).ReleaseWrench);
        Assert.True(attack.Step(Input(time: 34)).ReleaseWrench);
        Assert.False(attack.Step(Input(time: 50) with { ThrowActive = true }).ReleaseWrench);
        Assert.Null(attack.Step(Input(time: 70) with { ThrowActive = true }).Animation?.ExitLoopFrame);
        Assert.Equal(26, attack.Step(Input(time: 70)).Animation?.ExitLoopFrame);
        Assert.False(attack.Step(Input(time: 92)).Active);
    }

    [Fact]
    public void LateCrossPressCancelsGroundSwingIntoJumpWithoutEarlyCancellation()
    {
        var attack = new Rac1WrenchAttackSession();
        attack.Step(Input(true));
        Assert.True(attack.Step(Input(time: 20) with { JumpPressed = true }).Active);
        var cancelled = attack.Step(Input(time: 25) with { JumpPressed = true });
        Assert.True(cancelled.JumpCancelled);
        Assert.False(cancelled.Active);
    }

    [Fact]
    public void CometFlightUsesCubicTimeJerkAndReturnsToMovingHand()
    {
        var flight = new Rac1WrenchThrowSession();
        flight.Begin(new(0, 0, 1), new(1, 0, 0));
        var first = flight.Step(new(0, 0, 1));
        Assert.Equal(Rac1WrenchThrowSession.InitialStep, first.X, 7);
        Assert.Equal(23f / 60 - 170f / 216000, flight.StepLength, 7);
        double maximumX = first.X;
        int outward = 1;
        while (flight.NativeState == 10 && outward < 100)
        {
            maximumX = Math.Max(maximumX, flight.Step(new(0, 0, 1)).X);
            outward++;
        }
        Assert.Equal(31, outward);
        Assert.InRange(maximumX, 7.4, 8.2);
        var hand = new Rac1NativeVector3(-2, 3, 2);
        for (int i = 0; i < 100 && flight.Active; i++) flight.Step(hand);
        Assert.False(flight.Active);
        Assert.Equal(hand, flight.Position);
    }

    [Fact]
    public void WorldObstructionHalvesCurrentStepAndStartsReturn()
    {
        var flight = new Rac1WrenchThrowSession();
        flight.Begin(default, new(1, 0, 0));
        flight.Step(default, hitWorld: true);
        Assert.Equal(11, flight.NativeState);
        Assert.Equal((Rac1WrenchThrowSession.InitialStep - Rac1WrenchThrowSession.OutwardJerk) * .5f,
            flight.StepLength);
    }

    [Fact]
    public void ThrowBodyLoopUsesThreeUpdateSeamAndQueuedRecoveryFrame()
    {
        var clock = new Rac1AnimationFrameClock(new Dictionary<int, IReadOnlyList<double>>
        { [26] = Enumerable.Repeat(1d / 15, 45).ToArray() }, 26, 21, 1);
        clock.SetLoopWindow(6, 21);
        var seam = clock.Advance();
        Assert.Equal(21, seam.CurrentFrame);
        Assert.Equal(6, seam.NextFrame);
        Assert.Equal(0f, seam.Fraction);
        Assert.Equal(1f / 3, clock.Advance().Fraction);
        clock.QueueLoopExit(26);
        clock.Advance();
        var exit = clock.Advance();
        Assert.Equal(6, exit.CurrentFrame);
        Assert.Equal(26, exit.NextFrame);
        Assert.Equal(26, clock.Advance().CurrentFrame);
    }
}
