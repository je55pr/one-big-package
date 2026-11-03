using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class Rac1RatchetMovementTraceTests
{
    private static readonly PlayerContactFacts Grounded = new(true);
    private static readonly PlayerContactFacts Airborne = new(false);

    [Theory]
    [InlineData(1, 0.13816452026367188)]
    [InlineData(2, 0.13816452026367188)]
    [InlineData(3, 0.14207458496093750)]
    [InlineData(4, 0.14589500427246094)]
    [InlineData(5, 0.14962959289550781)]
    [InlineData(6, 0.15328598022460938)]
    [InlineData(7, 0.15686607360839844)]
    [InlineData(8, 0.15686607360839844)]
    public void JumpLaunch_ReplaysEveryRecoveredAnticipationHold(int heldTicks, double expected)
    {
        var controller = new Rac1RatchetMovementController();

        Rac1RatchetMovementController.StepResult step = default;
        for (int tick = 0; tick < Rac1RatchetMovementController.JumpAnticipationTicks; tick++)
        {
            bool held = tick < heldTicks;
            step = controller.Step(new PlayerControlIntent(0, 0, held, tick == 0), Grounded, _ => Math.PI / 2d);
        }

        Assert.Equal(Rac1RatchetMovementPhase.Rising, step.Phase);
        Assert.Equal(Rac1RatchetLocomotionState.Rising, step.LocomotionState);
        Assert.Equal(expected, step.Vertical, 12);
    }

    [Fact]
    public void HeldRise_ReplaysRecoveredNineTickAssistThenReleasedGravity()
    {
        var controller = new Rac1RatchetMovementController();
        for (int tick = 0; tick < Rac1RatchetMovementController.JumpAnticipationTicks; tick++)
            controller.Step(new PlayerControlIntent(0, 0, true, tick == 0), Grounded, _ => Math.PI / 2d);

        double[] expectedDecrements =
        [
            0.004741668701171875,
            0.004806518554687500,
            0.0048694610595703125,
            0.004928588867187500,
            0.0049839019775390625,
            0.005039215087890625,
            0.0050907135009765625,
            0.005138397216796875,
            0.0051860809326171875,
        ];

        double previous = controller.VerticalStep;
        foreach (double expected in expectedDecrements)
        {
            var step = controller.Step(new PlayerControlIntent(0, 0, true, false), Airborne);
            Assert.Equal(expected, previous - step.Vertical, 12);
            previous = step.Vertical;
        }

        var exhausted = controller.Step(new PlayerControlIntent(0, 0, true, false), Airborne);
        Assert.Equal(
            Rac1RatchetMovementController.ReleasedRiseGravityPerTick,
            previous - exhausted.Vertical,
            12);
    }

    [Fact]
    public void Fall_ReplaysRecoveredPerTickGravity()
    {
        var controller = new Rac1RatchetMovementController();
        var first = controller.Step(new PlayerControlIntent(), Airborne);
        var second = controller.Step(new PlayerControlIntent(), Airborne);

        Assert.Equal(-Rac1RatchetMovementController.FallGravityPerTick, first.Vertical, 12);
        Assert.Equal(-2d * Rac1RatchetMovementController.FallGravityPerTick, second.Vertical, 12);
        Assert.Equal(Rac1RatchetLocomotionState.Falling, second.LocomotionState);
        Assert.Equal(Rac1RatchetYawMode.Air, second.YawMode);
    }

    [Fact]
    public void DirectionalCrouch_ReportsRetailTurnInPlaceWithoutTranslation()
    {
        var controller = new Rac1RatchetMovementController();

        var turning = controller.Step(
            new PlayerControlIntent(1, 0, false, false, CrouchHeld: true),
            Grounded);
        var stationary = controller.Step(
            new PlayerControlIntent(0, 0, false, false, CrouchHeld: true),
            Grounded);

        Assert.Equal(0d, turning.PlanarMagnitude, 12);
        Assert.Equal(Rac1RatchetLocomotionState.CrouchTurning, turning.LocomotionState);
        Assert.Equal(Rac1RatchetYawMode.CrouchTurn, turning.YawMode);
        Assert.Equal(0d, stationary.PlanarMagnitude, 12);
        Assert.Equal(Rac1RatchetLocomotionState.Crouched, stationary.LocomotionState);
        Assert.Equal(Rac1RatchetYawMode.GroundStartup, stationary.YawMode);
    }

    [Fact]
    public void ScriptedRunStopCrouchTrace_ReplaysRecoveredCheckpoints()
    {
        var controller = new Rac1RatchetMovementController();
        var run = new PlayerControlIntent(0, 1, false, false);
        var release = new PlayerControlIntent();
        var crouchTurn = new PlayerControlIntent(1, 0, false, false, CrouchHeld: true);

        Rac1RatchetMovementController.StepResult step = default;
        for (int tick = 0; tick < 24; tick++)
            step = controller.Step(run, Grounded, _ => Math.PI / 2d);

        Assert.Equal(24d * Rac1RatchetMovementController.GroundAccelerationPerTick, step.PlanarMagnitude, 12);
        Assert.Equal(Rac1RatchetLocomotionState.Moving, step.LocomotionState);

        for (int tick = 0; tick < 6; tick++)
            step = controller.Step(release, Grounded, _ => Math.PI / 2d);

        Assert.Equal(0.03d, step.PlanarMagnitude, 12);

        for (int tick = 0; tick < 5; tick++)
            step = controller.Step(crouchTurn, Grounded, _ => Math.PI / 2d);

        Assert.Equal(0.03d - (5d * Rac1RatchetMovementController.CrouchDecelerationPerTick), step.PlanarMagnitude, 12);
        Assert.Equal(Rac1RatchetLocomotionState.CrouchTurning, step.LocomotionState);
        Assert.Equal(Rac1RatchetYawMode.CrouchTurn, step.YawMode);
    }

    [Fact]
    public void GroundAcceleration_CrossesObservedStartupToRunYawBoundaryOnNineteenthTick()
    {
        var controller = new Rac1RatchetMovementController();
        var run = new PlayerControlIntent(0, 1, false, false);

        Rac1RatchetMovementController.StepResult step = default;
        for (int tick = 0; tick < 18; tick++)
            step = controller.Step(run, Grounded, _ => Math.PI / 2d);

        Assert.Equal(0.0375d, step.PlanarMagnitude, 12);
        Assert.Equal(Rac1RatchetYawMode.GroundStartup, step.YawMode);

        step = controller.Step(run, Grounded, _ => Math.PI / 2d);

        Assert.True(step.PlanarMagnitude >= Rac1RatchetMovementController.GroundRunYawMinimumPlanarStep);
        Assert.Equal(Rac1RatchetYawMode.GroundRun, step.YawMode);
    }

    [Fact]
    public void RunningJump_UsesSequenceSevenYawFromAnticipationThroughLaunch()
    {
        var controller = new Rac1RatchetMovementController();
        var run = new PlayerControlIntent(0, 1, false, false);
        for (int tick = 0; tick < 80; tick++)
            controller.Step(run, Grounded, _ => Math.PI / 2d);

        var step = controller.Step(new PlayerControlIntent(0, 1, true, true), Grounded, _ => Math.PI / 2d);
        Assert.Equal(Rac1RatchetMovementPhase.JumpAnticipation, step.Phase);
        Assert.Equal(Rac1RatchetYawMode.Air, step.YawMode);

        for (int tick = 1; tick < Rac1RatchetMovementController.JumpAnticipationTicks; tick++)
            step = controller.Step(new PlayerControlIntent(0, 1, true, false), Grounded, _ => Math.PI / 2d);

        Assert.Equal(Rac1RatchetMovementPhase.Rising, step.Phase);
        Assert.Equal(Rac1RatchetYawMode.Air, step.YawMode);
    }
}
