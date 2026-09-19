using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class Rac1RatchetMovementControllerTests
{
    private static readonly PlayerContactFacts Grounded = new(true);
    private static readonly PlayerContactFacts Airborne = new(false);

    [Fact]
    public void GroundRun_AcceleratesToRetailPlateau()
    {
        var controller = new Rac1RatchetMovementController();
        var input = new PlayerControlIntent(0, 1, false, false);

        var first = controller.Step(input, Grounded);
        Assert.Equal(Rac1RatchetMovementController.GroundAccelerationPerTick, first.PlanarMagnitude, 12);

        Rac1RatchetMovementController.StepResult step = first;
        for (int i = 1; i < 80; i++)
            step = controller.Step(input, Grounded);

        Assert.Equal(Rac1RatchetMovementController.MaximumPlanarStep, step.PlanarMagnitude, 12);
        Assert.Equal(0.09500919d, step.PlanarMagnitude, 8);
    }

    [Fact]
    public void GroundWalkRelease_ReplaysRetainedNonUniformStop()
    {
        var controller = new Rac1RatchetMovementController();
        var walk = new PlayerControlIntent(0, 0.75, false, false);
        for (int i = 0; i < 12; i++)
            controller.Step(walk, Grounded);

        Assert.Equal(Rac1RatchetMovementController.WalkPlanarStep, controller.PlanarY, 12);

        var release = new PlayerControlIntent(0, 0, false, false);
        double[] expected =
        [
            0.012627291d,
            0.010271503d,
            0.00230833d,
            0d,
        ];

        foreach (double expectedStep in expected)
        {
            var step = controller.Step(release, Grounded);
            Assert.Equal(expectedStep, step.PlanarMagnitude, 12);
        }

        Assert.Equal(0d, controller.Step(release, Grounded).PlanarMagnitude, 12);
    }

    [Fact]
    public void GroundRunRelease_ReplaysHandoffThenUsesSustainedDecayToExactZero()
    {
        var controller = new Rac1RatchetMovementController();
        var run = new PlayerControlIntent(0, 1, false, false);
        for (int i = 0; i < 80; i++)
            controller.Step(run, Grounded);

        var release = new PlayerControlIntent(0, 0, false, false);
        double[] handoff =
        [
            0.09500918950800079d,
            0.09264604078218618d,
            0.09028356772803570d,
            0.08573509352062955d,
        ];

        foreach (double expectedStep in handoff)
        {
            var handoffStep = controller.Step(release, Grounded);
            Assert.Equal(expectedStep, handoffStep.PlanarMagnitude, 12);
        }

        var sustained = controller.Step(release, Grounded);
        Assert.Equal(
            handoff[^1] - Rac1RatchetMovementController.GroundDecelerationPerTick,
            sustained.PlanarMagnitude,
            12);

        Rac1RatchetMovementController.StepResult step = sustained;
        for (int i = 0; i < 40; i++)
            step = controller.Step(release, Grounded);

        Assert.Equal(0d, step.PlanarMagnitude, 12);
    }

    [Theory]
    [InlineData(2, 1.2267837524414062)]
    [InlineData(6, 1.5016689300537110)]
    [InlineData(18, 2.0543766021728516)]
    [InlineData(30, 2.0543766021728516)]
    public void StationaryJump_ReplaysRetailVariableHeight(int heldTicks, double expectedApex)
    {
        var controller = new Rac1RatchetMovementController();
        double height = 0d;
        double apex = 0d;
        bool launched = false;

        for (int tick = 0; tick < 120; tick++)
        {
            bool held = tick < heldTicks;
            var input = new PlayerControlIntent(0, 0, held, tick == 0);
            var contact = launched ? Airborne : Grounded;
            var step = controller.Step(input, contact);
            if (step.Vertical > 0d || controller.Phase != Rac1RatchetMovementPhase.JumpAnticipation)
                launched |= step.Vertical != 0d;

            if (!launched)
                continue;

            height += step.Vertical;
            apex = System.Math.Max(apex, height);
            if (controller.Phase == Rac1RatchetMovementPhase.Falling && height <= 0d)
                break;
        }

        Assert.InRange(apex, expectedApex - 0.00003d, expectedApex + 0.00003d);
    }

    [Fact]
    public void StationaryJump_HasEightTickAnticipation()
    {
        var controller = new Rac1RatchetMovementController();
        for (int tick = 0; tick < Rac1RatchetMovementController.JumpAnticipationTicks - 1; tick++)
        {
            var step = controller.Step(new PlayerControlIntent(0, 0, true, tick == 0), Grounded);
            Assert.Equal(0d, step.Vertical);
        }

        var launch = controller.Step(new PlayerControlIntent(0, 0, true, false), Grounded);
        Assert.Equal(Rac1RatchetMovementPhase.Rising, launch.Phase);
        Assert.True(launch.Vertical > 0d);
    }

    [Fact]
    public void AirControl_UsesStrongerAccelerationAndGentleRelease()
    {
        var controller = new Rac1RatchetMovementController();
        var forward = new PlayerControlIntent(0, 1, false, false);

        var first = controller.Step(forward, Airborne);
        Assert.Equal(Rac1RatchetMovementController.AirAccelerationPerTick, first.PlanarMagnitude, 12);

        Rac1RatchetMovementController.StepResult step = first;
        for (int i = 0; i < 40; i++)
            step = controller.Step(forward, Airborne);
        Assert.Equal(Rac1RatchetMovementController.MaximumPlanarStep, step.PlanarMagnitude, 12);

        var released = controller.Step(new PlayerControlIntent(0, 0, false, false), Airborne);
        Assert.Equal(
            Rac1RatchetMovementController.MaximumPlanarStep -
            Rac1RatchetMovementController.AirDecelerationPerTick,
            released.PlanarMagnitude,
            12);
    }

    [Fact]
    public void LeavingGround_EntersProvenFallRecurrence()
    {
        var controller = new Rac1RatchetMovementController();
        var step = controller.Step(new PlayerControlIntent(0, 0, false, false), Airborne);

        Assert.Equal(Rac1RatchetMovementPhase.Falling, step.Phase);
        Assert.Equal(-Rac1RatchetMovementController.FallGravityPerTick, step.Vertical, 12);
    }

    [Fact]
    public void Landing_ClearsVerticalMotionButPreservesPlanarMotion()
    {
        var controller = new Rac1RatchetMovementController();
        controller.Step(new PlayerControlIntent(0, 1, true, true), Grounded);
        for (int i = 1; i < Rac1RatchetMovementController.JumpAnticipationTicks; i++)
            controller.Step(new PlayerControlIntent(0, 1, true, false), Grounded);

        var airborne = controller.Step(new PlayerControlIntent(0, 1, false, false), Airborne);
        Assert.NotEqual(0d, airborne.Vertical);
        double planar = airborne.PlanarMagnitude;

        var landed = controller.Step(new PlayerControlIntent(0, 1, false, false), Grounded);
        Assert.Equal(Rac1RatchetMovementPhase.Grounded, landed.Phase);
        Assert.Equal(0d, landed.Vertical);
        Assert.True(landed.PlanarMagnitude >= planar);
    }
    [Fact]
    public void GroundedCrouch_SuppressesNewTranslationAndJump()
    {
        var controller = new Rac1RatchetMovementController();
        var crouch = new PlayerControlIntent(1, 0, true, true, CrouchHeld: true);

        var step = controller.Step(crouch, Grounded);

        Assert.Equal(0d, step.PlanarMagnitude, 12);
        Assert.Equal(0d, step.Vertical, 12);
        Assert.Equal(Rac1RatchetMovementPhase.Grounded, step.Phase);
    }

    [Fact]
    public void Reset_ClearsMomentumAndJumpStateForRespawn()
    {
        var controller = new Rac1RatchetMovementController();
        var runJump = new PlayerControlIntent(0, 1, true, true);
        controller.Step(runJump, Grounded);
        for (int i = 1; i < Rac1RatchetMovementController.JumpAnticipationTicks; i++)
            controller.Step(new PlayerControlIntent(0, 1, true, false), Grounded);
        controller.Step(new PlayerControlIntent(0, 1, false, false), Airborne);

        controller.Reset();

        Assert.Equal(Rac1RatchetMovementPhase.Grounded, controller.Phase);
        Assert.Equal(Rac1RatchetLocomotionState.Idle, controller.LocomotionState);
        Assert.Equal(Rac1RatchetYawMode.GroundStartup, controller.YawMode);
        Assert.Equal(0d, controller.PlanarX, 12);
        Assert.Equal(0d, controller.PlanarY, 12);
        Assert.Equal(0d, controller.VerticalStep, 12);
    }

    [Fact]
    public void EnteringCrouch_DeceleratesExistingGroundMotionWithRetailRate()
    {
        var controller = new Rac1RatchetMovementController();
        var run = new PlayerControlIntent(0, 1, false, false);
        for (int i = 0; i < 32; i++)
            controller.Step(run, Grounded);

        double before = controller.PlanarY;
        var crouched = controller.Step(new PlayerControlIntent(0, 1, false, false, CrouchHeld: true), Grounded);

        Assert.Equal(before - Rac1RatchetMovementController.CrouchDecelerationPerTick, crouched.PlanarY, 9);
        Assert.True(crouched.PlanarMagnitude < before);
    }

    [Fact]
    public void RunningJump_ReplaysRetailAirTimeAndDistance()
    {
        var controller = new Rac1RatchetMovementController();
        var run = new PlayerControlIntent(0, 1, false, false);
        for (int i = 0; i < 80; i++)
            controller.Step(run, Grounded);

        Rac1RatchetMovementController.StepResult step = default;
        for (int tick = 0; tick < Rac1RatchetMovementController.JumpAnticipationTicks; tick++)
        {
            bool held = tick < 6;
            step = controller.Step(new PlayerControlIntent(0, 1, held, tick == 0), Grounded);
        }

        double height = step.Vertical;
        int displacementTicks = 1;
        while (height > 0d || controller.Phase != Rac1RatchetMovementPhase.Falling)
        {
            step = controller.Step(run, Airborne);
            height += step.Vertical;
            displacementTicks++;
        }

        double distance = displacementTicks * Rac1RatchetMovementController.MaximumPlanarStep;
        double sampledAirTime = (displacementTicks - 1) / Rac1RatchetMovementController.UpdateHz;
        Assert.Equal(37, displacementTicks);
        Assert.InRange(sampledAirTime, 0.599d, 0.601d);
        Assert.InRange(distance, 3.5152d, 3.5154d);
    }

}
