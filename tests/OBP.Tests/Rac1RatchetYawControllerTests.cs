using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class Rac1RatchetYawControllerTests
{
    [Fact]
    public void MovementTarget_UsesCurrentProvisionalControlRelativeFormula()
    {
        Assert.Equal(
            -Math.PI / 2d,
            Rac1RatchetYawController.BuildMovementTarget(0d, 1d, 0d),
            12);
        Assert.Equal(
            0d,
            Rac1RatchetYawController.BuildMovementTarget(0d, 1d, Math.PI / 2d),
            12);
        Assert.Equal(
            -Math.PI / 2d,
            Rac1RatchetYawController.BuildMovementTarget(1d, 0d, Math.PI / 2d),
            12);
    }

    [Fact]
    public void GroundTurn_WrapsAcrossPiByShortestError()
    {
        double initial = Math.PI - 0.05d;
        double target = -Math.PI + 0.05d;
        var controller = new Rac1RatchetYawController(initial);

        var step = controller.Step(
            0d,
            1d,
            target + (Math.PI / 2d),
            grounded: true);

        Assert.Equal(target, step.TargetYaw, 12);
        Assert.Equal(
            Rac1RatchetYawController.GroundErrorGain * 0.1d,
            Rac1RatchetYawController.WrapPi(step.CurrentYaw - initial),
            12);
    }

    [Fact]
    public void GroundTurn_AppliesCurrentProvisionalAccelerationAndDamping()
    {
        var controller = new Rac1RatchetYawController();
        double controlYaw = 1d + (Math.PI / 2d);

        var first = controller.Step(0d, 1d, controlYaw, grounded: true);
        Assert.Equal(Rac1RatchetYawController.GroundErrorGain, first.YawVelocity, 12);
        Assert.Equal(first.YawVelocity, first.CurrentYaw, 12);

        double expectedSecondVelocity = first.YawVelocity +
            (Rac1RatchetYawController.GroundErrorGain * (1d - first.CurrentYaw)) -
            (Rac1RatchetYawController.GroundVelocityDamping * first.YawVelocity);
        var second = controller.Step(0d, 1d, controlYaw, grounded: true);
        Assert.Equal(expectedSecondVelocity, second.YawVelocity, 12);
    }

    [Fact]
    public void GroundTurn_EnforcesCurrentProvisionalMaximumStep()
    {
        var controller = new Rac1RatchetYawController();
        for (int i = 0; i < 120; i++)
        {
            double desired = Rac1RatchetYawController.WrapPi(
                controller.CurrentYaw + Math.PI - 1e-6d);
            var step = controller.Step(
                0d,
                1d,
                desired + (Math.PI / 2d),
                grounded: true);
            Assert.True(Math.Abs(step.YawVelocity) <= Rac1RatchetYawController.GroundMaximumStep + 1e-12d);
        }

        Assert.Equal(
            Rac1RatchetYawController.GroundMaximumStep,
            Math.Abs(controller.YawVelocity),
            12);
    }

    [Fact]
    public void GroundTurn_ProtectsOvershootAndSnapsExactlyToTarget()
    {
        var controller = new Rac1RatchetYawController();
        double controlYaw = 1d + (Math.PI / 2d);
        for (int i = 0; i < 8; i++)
            controller.Step(0d, 1d, controlYaw, grounded: true);

        double target = Rac1RatchetYawController.WrapPi(controller.CurrentYaw + 0.001d);
        var step = controller.Step(
            0d,
            1d,
            target + (Math.PI / 2d),
            grounded: true);

        Assert.Equal(target, step.CurrentYaw, 12);
        Assert.Equal(0d, step.YawVelocity, 12);
    }

    [Fact]
    public void ZeroInput_RetainsCurrentYawAndClearsTurnVelocity()
    {
        var controller = new Rac1RatchetYawController();
        controller.Step(0d, 1d, 1d + (Math.PI / 2d), grounded: true);
        double before = controller.CurrentYaw;

        var step = controller.Step(0d, 0d, -2d, grounded: true);

        Assert.Equal(-2d, step.ControlYaw, 12);
        Assert.Equal(before, step.TargetYaw, 12);
        Assert.Equal(before, step.CurrentYaw, 12);
        Assert.Equal(0d, step.YawVelocity, 12);
    }

    [Fact]
    public void GroundTurn_ConvergesExactlyToFixedTarget()
    {
        var controller = new Rac1RatchetYawController();
        double target = 1.2d;
        for (int i = 0; i < 240; i++)
            controller.Step(0d, 1d, target + (Math.PI / 2d), grounded: true);

        Assert.Equal(target, controller.CurrentYaw, 12);
        Assert.Equal(0d, controller.YawVelocity, 12);
    }

    [Fact]
    public void CameraRotationAlone_DoesNotRedirectWrenchFacing()
    {
        var yaw = new Rac1RatchetYawController(0.6627015d);
        var wrench = new Rac1WrenchCombatController();
        var before = wrench.ResolveFirstSwingFacing(yaw.CurrentYaw);

        yaw.Step(0d, 0d, -2.4d, grounded: true);
        var after = wrench.ResolveFirstSwingFacing(yaw.CurrentYaw);

        Assert.Equal(before.X, after.X, 12);
        Assert.Equal(before.Y, after.Y, 12);
        Assert.Equal(0.6627015d, yaw.CurrentYaw, 12);
        Assert.Equal(-2.4d, yaw.ControlYaw, 12);
    }

    [Fact]
    public void GroundedCrouchInput_CanTurnWithoutCreatingTranslation()
    {
        var movement = new Rac1RatchetMovementController();
        var yaw = new Rac1RatchetYawController();
        var crouched = movement.Step(
            new PlayerControlIntent(1d, 0d, false, false, CrouchHeld: true),
            new PlayerContactFacts(true));
        var turn = yaw.Step(1d, 0d, 0d, grounded: true);

        Assert.Equal(0d, crouched.PlanarMagnitude, 12);
        Assert.NotEqual(0d, turn.CurrentYaw);
    }

    [Fact]
    public void AirTurn_UsesCurrentProvisionalRecurrenceAndMaximumStep()
    {
        var controller = new Rac1RatchetYawController();
        double controlYaw = 1d + (Math.PI / 2d);

        var first = controller.Step(0d, 1d, controlYaw, grounded: false);
        Assert.Equal(0.04d, first.YawVelocity, 12);
        Assert.Equal(0.04d, first.CurrentYaw, 12);

        var second = controller.Step(0d, 1d, controlYaw, grounded: false);
        Assert.Equal(0.0704d, second.YawVelocity, 12);

        for (int i = 0; i < 80; i++)
        {
            double desired = Rac1RatchetYawController.WrapPi(
                controller.CurrentYaw + Math.PI - 1e-6d);
            var step = controller.Step(
                0d,
                1d,
                desired + (Math.PI / 2d),
                grounded: false);
            Assert.True(Math.Abs(step.YawVelocity) <= Rac1RatchetYawController.AirMaximumStep + 1e-12d);
        }

        Assert.Equal(Rac1RatchetYawController.AirMaximumStep, Math.Abs(controller.YawVelocity), 12);
    }
}
