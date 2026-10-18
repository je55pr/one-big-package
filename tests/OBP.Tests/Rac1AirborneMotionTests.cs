using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1AirborneMotionTests
{
    [Fact]
    public void NewAirActionCopiesContactStepAndResetsImpulseHistory()
    {
        var profile = Rac1AirborneJumpProfile.Create(Rac1AirborneJumpKind.DirectionalFlip, fastEntry: true);
        var session = new Rac1AirborneMotionSession(profile, -0.12f);
        Assert.Equal(-0.12f, session.VerticalStep);
        Assert.Equal(0, session.AppliedImpulse);
        var launch = session.Step(new(1, true, -0.12f));
        float height = 3.2f + ((3.25f - 3.2f) / 14);
        float impulse = MathF.Sqrt((height + height) * (29.7f * (1f / 3600f)));
        Assert.True(launch.HasLaunched);
        Assert.Equal(impulse, session.AppliedImpulse);
        Assert.Equal((-0.12f + impulse) - session.GravityPerUpdate, launch.VerticalStep);
    }

    [Fact]
    public void HoldAddsOnlyTheDifferenceBetweenHeightDerivedImpulses()
    {
        var session = new Rac1AirborneMotionSession(
            Rac1AirborneJumpProfile.Create(Rac1AirborneJumpKind.DirectionalFlip, fastEntry: true), 0);
        var first = session.Step(new(1, true, 0));
        float firstImpulse = session.AppliedImpulse;
        var second = session.Step(new(2, true, first.VerticalStep));
        float secondImpulse = session.AppliedImpulse;
        Assert.InRange(secondImpulse - firstImpulse, 0.0001f, 0.001f);
        Assert.Equal(first.VerticalStep + (secondImpulse - firstImpulse) - session.GravityPerUpdate,
            second.VerticalStep, 6);
        session.Step(new(3, false, second.VerticalStep));
        Assert.Equal(secondImpulse, session.AppliedImpulse);
    }

    [Fact]
    public void NativeLaunchAgeSeparatesSupportRequestFromImpulse()
    {
        var session = new Rac1AirborneMotionSession(
            Rac1AirborneJumpProfile.Create(Rac1AirborneJumpKind.HeliPackLong), 0);
        for (int age = 0; age < 5; age++)
        {
            var step = session.Step(new(age, false, 0));
            Assert.False(step.HasLaunched);
            Assert.Equal(-(48f * (1f / 3600f)), step.VerticalStep);
        }
        Assert.True(session.Step(new(5, false, 0)).HasLaunched);
    }

    [Fact]
    public void ThrusterHighUsesFiveUpdateInitialScriptAndTwentySevenUpdateCoast()
    {
        var session = new Rac1AirborneMotionSession(
            Rac1AirborneJumpProfile.Create(Rac1AirborneJumpKind.ThrusterPackHigh), 0);
        float prior = 0;
        for (int age = 0; age < 9; age++)
            prior = session.Step(new(age, false, prior)).VerticalStep;
        var launch = session.Step(new(9, false, prior));
        prior = launch.VerticalStep;
        for (int age = 10; age < 14; age++)
        {
            float current = session.Step(new(age, false, prior)).VerticalStep;
            Assert.Equal(prior + (151f / 3600f) - session.GravityPerUpdate, current, 6);
            prior = current;
        }
        var coast = session.Step(new(14, false, prior));
        Assert.Equal(prior - session.GravityPerUpdate, coast.VerticalStep, 6);
    }

    [Fact]
    public void CeilingInterruptionCannotReapplyHeldImpulseAndLandingEndsSession()
    {
        var session = new Rac1AirborneMotionSession(
            Rac1AirborneJumpProfile.Create(Rac1AirborneJumpKind.DirectionalFlip, fastEntry: true), 0);
        session.Step(new(1, true, 0));
        var hit = session.Step(new(2, true, 0, HitCeiling: true));
        Assert.True(hit.VerticalStep <= 0);
        var next = session.Step(new(3, true, hit.VerticalStep));
        Assert.True(next.VerticalStep < hit.VerticalStep);
        Assert.Equal(0, session.Step(new(4, true, 0, LandedAfterLaunch: true)).VerticalStep);
        Assert.True(session.Finished);
        Assert.Equal(0, session.Step(new(5, true, 0)).VerticalStep);
    }

    [Fact]
    public void ContactRelativeFallClampAndTerminalStepAreIndependentOfLaunchHeight()
    {
        var session = new Rac1AirborneMotionSession(
            Rac1AirborneJumpProfile.Create(Rac1AirborneJumpKind.DirectionalFlip, fastEntry: true), -2);
        var first = session.Step(new(1, false, -2));
        Assert.Equal(-(50f * (1f / 60f)), first.VerticalStep);
        var blocked = session.Step(new(2, false, 0));
        Assert.Equal(-0.1f, blocked.VerticalStep);
    }

    [Fact]
    public void LedgeAnticipationRetainsContactVerticalInsteadOfGroundSupportRequest()
    {
        var session = new Rac1AirborneMotionSession(
            Rac1AirborneJumpProfile.Create(Rac1AirborneJumpKind.LedgeJump), 0);
        for (int age = 0; age < 10; age++)
            Assert.Equal(0, session.Step(new(age, false, 0)).VerticalStep);
        Assert.True(session.Step(new(10, false, 0)).HasLaunched);
    }

    [Theory]
    [InlineData(0, 28)]
    [InlineData(1, 29)]
    [InlineData(2, 28)]
    [InlineData(3, 31)]
    public void FlipSequenceUsesRecoveredDirectionRemap(int direction, int expected)
    {
        var normal = Rac1AirborneJumpProfile.Create(Rac1AirborneJumpKind.DirectionalFlip, flipDirection: direction);
        var quick = Rac1AirborneJumpProfile.Create(Rac1AirborneJumpKind.DirectionalFlip, true, direction);
        Assert.Equal(expected, normal.EntrySequence);
        Assert.Equal(expected, quick.EntrySequence);
        Assert.Equal((5, 0, 5), (normal.LaunchAge, normal.EntryFrame, normal.EntryBlendTicks));
        Assert.Equal((1, 1, 4), (quick.LaunchAge, quick.EntryFrame, quick.EntryBlendTicks));
    }

    [Fact]
    public void RejectsModifiedProfilesAndInvalidFacts()
    {
        var profile = Rac1AirborneJumpProfile.Create(Rac1AirborneJumpKind.HeliPackHigh);
        Assert.Throws<ArgumentException>(() => new Rac1AirborneMotionSession(profile with { LaunchAge = 0 }, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rac1AirborneMotionSession(profile, float.NaN));
        var session = new Rac1AirborneMotionSession(profile, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Step(new(-1, true, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Step(new(0, true, float.NaN)));
    }
}
