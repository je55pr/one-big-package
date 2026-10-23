using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1WaterMovementSessionTests
{
    private static Rac1WaterMovementInput Falling(float depth = 4) => new(
        new(true, 56, 56 - depth), 55.65f, new(.04, .01, -.1), -.1f, 0, 0, 0,
        MovementDomain: 4, MovementAction: 7);

    [Fact]
    public void WaterAdmissionUsesNativeFootOffsetDepthAndApexInsteadOfStandingCapsuleDepth()
    {
        Assert.False(new Rac1WaterMovementSession().Step(Falling(.1875f)).Active);
        Assert.False(new Rac1WaterMovementSession().Step(Falling(.8f)).Active);
        Assert.False(new Rac1WaterMovementSession().Step(Falling() with { HasApexed = false }).Active);
        Assert.False(new Rac1WaterMovementSession().Step(Falling() with { Contact = default }).Active);
        Assert.False(new Rac1WaterMovementSession().Step(Falling() with { PositionHeight = 56.12f }).Active);
        var surface = new Rac1WaterMovementSession().Step(Falling());
        Assert.True(surface.Active);
        Assert.Equal(18, surface.State.NativeDomain);
        Assert.Equal(0x37, surface.State.NativeAction);
        Assert.Equal(58, surface.Animation!.Value.Sequence);
        Assert.Equal(22, surface.Animation.Value.BlendTicks);
        Assert.Equal(.6f, surface.Animation.Value.PlaybackMultiplier);
        Assert.Equal(3, surface.Sound);
        Assert.False(new Rac1WaterMovementSession().Step(Falling() with
        { MovementAction = 0x12, HasApexed = false, PositionHeight = 54 }).Active);
    }

    [Fact]
    public void PeriodicSubmergedQueryEntersUnderwaterIdleWithoutAnInventedDepthBox()
    {
        var session = new Rac1WaterMovementSession();
        var input = Falling() with
        {
            Contact = new(true, 56, 52, SurfaceUnderfoot: false),
            PositionHeight = 54,
            ContactMotion = default,
            MovementDomain = 2
        };
        for (int i = 0; i < 15; i++) Assert.False(session.Step(input).Active);
        var submerged = session.Step(input);
        Assert.Equal(17, submerged.State.NativeDomain);
        Assert.Equal(0x34, submerged.State.NativeAction);
        Assert.Equal(60, submerged.Animation!.Value.Sequence);
    }

    [Theory]
    [InlineData(0x1c)]
    [InlineData(7)]
    public void AboveFootPeriodicRayCannotStealAnAirborneLaunchAtTheWaterline(int action)
    {
        // Native helper adds +1.3 to the ray end, so water only .18 above
        // the feet is outside it, independently of the sixteen-frame phase.
        var session = new Rac1WaterMovementSession();
        var input = Falling() with
        {
            Contact = new(true, 82.25f, 78.5f),
            PositionHeight = 82.07f,
            ContactMotion = default,
            RequestedVerticalStep = 0,
            MovementDomain = 4,
            MovementAction = action,
            HasApexed = false,
        };
        for (int i = 0; i < 64; i++) Assert.False(session.Step(input).Active);
    }

    [Fact]
    public void LandingBobAndContactCarryAgreeWithOriginalNovalisInstructionWitness()
    {
        // Original 2370b8 / 240c78, tools/rac1-water-native-oracle.cpp.
        // Native seed: action37 age0, z55.65, bob-.23, bobV-.037,
        // water56, carry(.04,.01,0). IEEE/VU rounding differs by a few ULP.
        var result = new Rac1WaterMovementSession().Step(Falling());
        Assert.InRange(result.Request.X, .0388680, .0388683);
        Assert.InRange(result.Request.Y, .0097170, .0097171);
        Assert.InRange(55.65 + result.Request.Z, 55.61580, 55.61583);
        Assert.Equal(10_000, result.State.Oxygen);
    }

    [Fact]
    public void SurfaceCrossUsesPressHistoryAndDedicatedJumpExitRatherThanUpwardSwimming()
    {
        var session = new Rac1WaterMovementSession();
        var input = Falling() with { CrossHeld = true };
        Assert.True(session.Step(input).Active);
        for (int i = 0; i < 20; i++) Assert.True(session.Step(input).Active);
        var jump = session.Step(input with { CrossPressed = true });
        Assert.False(jump.Active);
        Assert.Equal(Rac1WaterExit.Jump, jump.Exit);
        Assert.Null(jump.Animation);
        session.Reset();
        session.Step(Falling());
        // Fresh Cross is buffered eight updates, but idle37 only admits it
        // after age10. An early press must expire, not become a delayed jump.
        session.Step(Falling() with { CrossPressed = true });
        for (int i = 0; i < 20; i++) Assert.True(session.Step(Falling()).Active);
    }

    [Fact]
    public void SurfaceMovingHasStrokeAnimationAndRetainsItsMinimumStrokeBeforeStopping()
    {
        var session = new Rac1WaterMovementSession();
        var input = Falling();
        session.Step(input);
        for (int i = 0; i < 11; i++) session.Step(input);
        var moving = session.Step(input with { Magnitude = 1 });
        Assert.Equal(0x36, moving.State.NativeAction);
        Assert.Equal(101, moving.Animation!.Value.Sequence);
        Assert.Equal(17, moving.Animation.Value.BlendTicks);
        long generation = moving.Animation.Value.EntryGeneration;
        input = input with { Animation = new(101, 4, .1f) };
        var stroke = session.Step(input);
        Assert.True(stroke.Request.X > 0);
        Assert.Equal(generation, stroke.Animation!.Value.EntryGeneration);
        for (int i = 0; i < 100; i++) stroke = session.Step(input);
        Assert.Equal(0x37, stroke.State.NativeAction);
        Assert.Equal(58, stroke.Animation!.Value.Sequence);
        Assert.NotEqual(generation, stroke.Animation.Value.EntryGeneration);
    }

    [Theory]
    [InlineData(false, 0x33, 59)]
    [InlineData(true, 0x35, 61)]
    public void DiveAndHydroPackRequireActualOwnership(bool ownsHydro, int action, int sequence)
    {
        var session = new Rac1WaterMovementSession();
        session.Step(Falling());
        var dive = session.Step(Falling() with
        {
            SquareHeld = true,
            SquarePressed = true,
            CrouchHeld = true,
            OwnsHydroPack = ownsHydro,
        });
        Assert.Equal(action, dive.State.NativeAction);
        Assert.Equal(sequence, dive.Animation!.Value.Sequence);
        Assert.True(session.Diving);
        Assert.True(dive.Pitch > 0); // Native +pitch points the nose down.
        for (int i = 0; i < 40; i++) session.Step(Falling() with { SquareHeld = true });
        Assert.False(session.Diving);
    }

    [Fact]
    public void UnderwaterCrossAscendsBeforeSurfaceAdmissionRefillsOxygen()
    {
        var session = new Rac1WaterMovementSession();
        session.Step(Falling());
        var input = Falling() with { PositionHeight = 53, SquareHeld = true, SquarePressed = true };
        session.Step(input);
        input = input with { SquareHeld = false, SquarePressed = false, CrossHeld = true, Animation = new(59, 12, .25f) };
        Rac1WaterMovementResult state = default;
        for (int i = 0; i < 150; i++) state = session.Step(input);
        Assert.Equal(Rac1WaterDomain.Underwater, state.State.Water);
        Assert.True(state.Pitch < -.8f);
        Assert.True(state.Request.Z > 0);
        Assert.True(state.State.Oxygen < 10_000);
        var surfaced = session.Step(input with { PositionHeight = 55.7f, ContactMotion = new(0, 0, .04) });
        Assert.Equal(Rac1WaterDomain.Surface, surfaced.State.Water);
        Assert.Equal(10_000, surfaced.State.Oxygen);
    }

    [Fact]
    public void OxygenMaskPreventsDrainAndDrowningWaitsForItsOwnAnimationToFinish()
    {
        var session = new Rac1WaterMovementSession();
        session.Step(Falling());
        var input = Falling() with { PositionHeight = 53, SquareHeld = true, SquarePressed = true, OwnsOxygenMask = true };
        session.Step(input);
        input = input with { SquarePressed = false };
        for (int i = 0; i < 950; i++) Assert.Equal(10_000, session.Step(input).State.Oxygen);
        input = input with { OwnsOxygenMask = false };
        Rac1WaterMovementResult state = default;
        for (int i = 0; i < 910; i++) state = session.Step(input);
        Assert.True(state.State.Drowning);
        Assert.Equal(20, state.State.NativeDomain);
        Assert.Equal(95, state.Animation!.Value.Sequence);
        Assert.False(state.DeathAnimationCompleted);
        var finished = session.Step(input with { Animation = new(95, 100, .5f, CycleCompleted: true) });
        Assert.True(finished.DeathAnimationCompleted);
        session.Reset();
        Assert.False(session.Active);
        Assert.Equal(10_000, session.Oxygen);
    }

    [Fact]
    public void ShoreAndMissingWaterReleaseOwnershipInsteadOfKeepingAnOldAirPose()
    {
        var session = new Rac1WaterMovementSession();
        session.Step(Falling());
        var shore = session.Step(Falling(.3f));
        Assert.False(shore.Active);
        Assert.Equal(Rac1WaterExit.Ground, shore.Exit);
        session.Step(Falling());
        var left = session.Step(Falling() with { Contact = default });
        Assert.False(left.Active);
        Assert.Equal(Rac1WaterExit.Fall, left.Exit);
        Assert.Null(left.Animation);
    }

    [Fact]
    public void EmptyOxygenStillAdmitsTheNativeNearSurfaceEscapeAndCeilingForcesDiving()
    {
        var session = new Rac1WaterMovementSession();
        session.Step(Falling());
        var ceiling = session.Step(Falling() with { Contact = new(true, 56, 52, SurfaceCeilingBlocked: true) });
        Assert.Equal(0x33, ceiling.State.NativeAction);
        Assert.Equal(3, ceiling.Sound);
        var input = Falling() with { PositionHeight = 53, CrossHeld = true, Animation = new(59, 12, .25f) };
        while (session.Oxygen > 11) session.Step(input);
        var escaping = session.Step(input with { PositionHeight = 54.5f, ContactMotion = new(0, 0, .04) });
        Assert.Equal(0, escaping.State.Oxygen);
        Assert.False(escaping.State.Drowning);
        var blocked = session.Step(input with { PositionHeight = 54.5f, ContactMotion = new(0, 0, .01) });
        Assert.True(blocked.State.Drowning);
    }

    [Theory]
    [InlineData(1, 15, 0)]
    [InlineData(4, 2, 37)]
    public void WaterExitUsesDepthSpecificSourceArcAndDoesNotReenterDuringItsInitialDip(
        float depth, int launch, int scriptEnd)
    {
        var profile = Rac1AirborneJumpProfile.Create(Rac1AirborneJumpKind.WaterExit, environmentDepth: depth);
        Assert.Equal(launch, profile.LaunchAge);
        Assert.Equal(scriptEnd, profile.ScriptEndAge);
        Assert.Equal(114, profile.EntrySequence);
        Assert.Equal(1, profile.EntryFrame);
        var motion = new Rac1AirborneMotionSession(profile, 0);
        float vertical = 0, height = 0, peak = 0;
        for (int age = 0; age < 90; age++)
        {
            vertical = motion.Step(new(age, false, vertical)).VerticalStep;
            height += vertical;
            peak = MathF.Max(peak, height);
        }
        Assert.True(peak > 1);
    }
}
