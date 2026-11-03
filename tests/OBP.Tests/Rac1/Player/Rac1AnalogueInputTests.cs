using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class Rac1AnalogueInputTests
{
    private static readonly PlayerContactFacts Grounded = new(true);
    private static readonly PlayerContactFacts Airborne = new(false);

    public static TheoryData<byte, byte, double, double, bool, Rac1AnalogueSpeedBand> RawWitnesses => new()
    {
        { 127, 127, 0d, 0d, false, Rac1AnalogueSpeedBand.Inactive },
        { 127, 61, 0d, 18d / 76d, false, Rac1AnalogueSpeedBand.Inactive },
        { 127, 60, 0d, 19d / 76d, true, Rac1AnalogueSpeedBand.Walk },
        { 188, 188, 13d / 76d, -13d / 76d, false, Rac1AnalogueSpeedBand.Inactive },
        { 189, 189, 14d / 76d, -14d / 76d, true, Rac1AnalogueSpeedBand.Walk },
        { 127, 237, 0d, -62d / 76d, true, Rac1AnalogueSpeedBand.Walk },
        { 127, 238, 0d, -63d / 76d, true, Rac1AnalogueSpeedBand.Run },
        { 219, 219, 44d / 76d, -44d / 76d, true, Rac1AnalogueSpeedBand.Walk },
        { 220, 220, 45d / 76d, -45d / 76d, true, Rac1AnalogueSpeedBand.Run },
        { 255, 255, 1d, -1d, true, Rac1AnalogueSpeedBand.Run },
    };

    [Theory]
    [MemberData(nameof(RawWitnesses))]
    public void RawByteConditioner_ReplaysRetainedWitnesses(
        byte rawX,
        byte rawY,
        double expectedX,
        double expectedY,
        bool active,
        Rac1AnalogueSpeedBand band)
    {
        var input = Rac1AnalogueInput.ConditionRawLeftStick(rawX, rawY);

        Assert.Equal(expectedX, input.X, 12);
        Assert.Equal(expectedY, input.Y, 12);
        Assert.Equal(active, input.IsActive);
        Assert.Equal(band, input.SpeedBand);
        Assert.Equal(
            Math.Min(Math.Sqrt((expectedX * expectedX) + (expectedY * expectedY)), 1d),
            input.Magnitude,
            12);
    }

    [Theory]
    [MemberData(nameof(RawWitnesses))]
    public void UnitAxisBoundary_MatchesLiteralRawByteConditioner(
        byte rawX,
        byte rawY,
        double expectedX,
        double expectedY,
        bool active,
        Rac1AnalogueSpeedBand band)
    {
        var literal = Rac1AnalogueInput.ConditionRawLeftStick(rawX, rawY);
        var unit = Rac1AnalogueInput.ConditionUnitAxes(
            RightUnit(rawX),
            ForwardUnit(rawY));

        Assert.Equal(expectedX, unit.X, 12);
        Assert.Equal(expectedY, unit.Y, 12);
        Assert.Equal(literal.UncappedMagnitude, unit.UncappedMagnitude, 12);
        Assert.Equal(active, unit.IsActive);
        Assert.Equal(band, unit.SpeedBand);
    }

    [Fact]
    public void UnitAxisBoundary_DenselyMatchesEveryLiteralLeftStickBytePair()
    {
        for (int rawX = byte.MinValue; rawX <= byte.MaxValue; rawX++)
        {
            for (int rawY = byte.MinValue; rawY <= byte.MaxValue; rawY++)
            {
                var literal = Rac1AnalogueInput.ConditionRawLeftStick((byte)rawX, (byte)rawY);
                var unit = Rac1AnalogueInput.ConditionUnitAxes(
                    RightUnit((byte)rawX),
                    ForwardUnit((byte)rawY));

                Assert.Equal(literal.X, unit.X, 12);
                Assert.Equal(literal.Y, unit.Y, 12);
                Assert.Equal(literal.UncappedMagnitude, unit.UncappedMagnitude, 12);
                Assert.Equal(literal.Magnitude, unit.Magnitude, 12);
                Assert.Equal(literal.IsActive, unit.IsActive);
                Assert.Equal(literal.SpeedBand, unit.SpeedBand);
            }
        }
    }

    [Fact]
    public void PlayerControlIntentBoundary_ConditionsInsideRac1Controller()
    {
        var intent = IntentFromRaw(220, 220);
        var literal = Rac1AnalogueInput.ConditionRawLeftStick(220, 220);
        var controller = new Rac1RatchetMovementController();

        Assert.NotEqual(literal.X, intent.PlanarX);
        Assert.NotEqual(literal.Y, intent.PlanarY);

        controller.Step(intent, Grounded, _ => 0d);

        Assert.Equal(literal, controller.AnalogueInput);
    }

    [Fact]
    public void GroundDeadZone_NonzeroRawIntentDoesNotMove()
    {
        var controller = new Rac1RatchetMovementController();
        var input = IntentFromRaw(127, 61);

        for (int i = 0; i < 20; i++)
            controller.Step(input, Grounded);

        Assert.Equal(0d, controller.PlanarX, 12);
        Assert.Equal(0d, controller.PlanarY, 12);
        Assert.Equal(0d, controller.TargetPlanarStep, 12);
        Assert.False(controller.AnalogueInput.IsActive);
        Assert.Equal(Rac1RatchetLocomotionState.Idle, controller.LocomotionState);
    }

    [Theory]
    [InlineData(127, 60)]
    [InlineData(127, 237)]
    [InlineData(219, 219)]
    public void GroundWalkBand_SettlesOnRecoveredPlateau(byte rawX, byte rawY)
    {
        var controller = new Rac1RatchetMovementController();
        var input = IntentFromRaw(rawX, rawY);
        Rac1RatchetMovementController.StepResult step = default;

        for (int i = 0; i < 20; i++)
            step = controller.Step(input, Grounded, _ => 0d);

        Assert.Equal(Rac1RatchetMovementController.WalkPlanarStep, step.PlanarMagnitude, 12);
        Assert.Equal(Rac1RatchetMovementController.WalkPlanarStep, controller.TargetPlanarStep, 12);
        Assert.Equal(Rac1AnalogueSpeedBand.Walk, controller.AnalogueInput.SpeedBand);
    }

    [Theory]
    [InlineData(127, 238)]
    [InlineData(127, 255)]
    [InlineData(220, 220)]
    [InlineData(255, 255)]
    public void GroundRunBand_PreservesFullRetailCap(byte rawX, byte rawY)
    {
        var controller = new Rac1RatchetMovementController();
        var input = IntentFromRaw(rawX, rawY);
        Rac1RatchetMovementController.StepResult step = default;
        for (int i = 0; i < 80; i++)
            step = controller.Step(input, Grounded, _ => 0d);

        Assert.Equal(Rac1RatchetMovementController.MaximumPlanarStep, step.PlanarMagnitude, 12);
        Assert.Equal(Rac1RatchetMovementController.MaximumPlanarStep, controller.TargetPlanarStep, 12);
        Assert.Equal(Rac1AnalogueSpeedBand.Run, controller.AnalogueInput.SpeedBand);
    }

    [Fact]
    public void FullDiagonal_HasNoSquareStickSpeedBoost()
    {
        var controller = new Rac1RatchetMovementController();
        var input = IntentFromRaw(255, 255);
        Rac1RatchetMovementController.StepResult step = default;

        for (int i = 0; i < 80; i++)
            step = controller.Step(input, Grounded, _ => -Math.PI / 4d);

        double component = Rac1RatchetMovementController.MaximumPlanarStep / Math.Sqrt(2d);
        Assert.Equal(component, step.PlanarX, 12);
        Assert.Equal(-component, step.PlanarY, 12);
        Assert.Equal(Rac1RatchetMovementController.MaximumPlanarStep, step.PlanarMagnitude, 12);
    }

    [Fact]
    public void AirConditionerRunsBeforeEngineNeutralControlBasis()
    {
        var controller = new Rac1RatchetMovementController();
        var input = new PlayerControlIntent(
            0d,
            1d,
            false,
            false,
            PlanarBasis: new PlayerPlanarBasis(0d, 1d, -1d, 0d));

        var step = controller.Step(input, Airborne);

        Assert.Equal(-Rac1RatchetMovementController.AirAccelerationPerTick, step.PlanarX, 12);
        Assert.Equal(0d, step.PlanarY, 12);
    }

    [Theory]
    [InlineData(127, 60, 0.023759150)]
    [InlineData(189, 60, 0.029516687)]
    [InlineData(219, 219, 0.077775483)]
    [InlineData(127, 238, 0.078740573)]
    [InlineData(220, 220, 0.079549722)]
    public void PartialStickAirControl_ScalesTargetByConditionedMagnitude(
        byte rawX,
        byte rawY,
        double observedRetailStep)
    {
        var controller = new Rac1RatchetMovementController();
        var input = IntentFromRaw(rawX, rawY);
        var conditioned = Rac1AnalogueInput.ConditionRawLeftStick(rawX, rawY);

        double expectedTarget =
            Rac1RatchetMovementController.MaximumPlanarStep * conditioned.Magnitude;

        var first = controller.Step(input, Airborne);
        Assert.Equal(Rac1RatchetMovementController.AirAccelerationPerTick, first.PlanarMagnitude, 12);
        Assert.Equal(expectedTarget, controller.TargetPlanarStep, 12);
        Assert.Equal(conditioned.SpeedBand, controller.AnalogueInput.SpeedBand);

        Rac1RatchetMovementController.StepResult step = first;
        for (int i = 0; i < 40; i++)
            step = controller.Step(input, Airborne);

        Assert.Equal(expectedTarget, step.PlanarMagnitude, 12);
        Assert.InRange(step.PlanarMagnitude, observedRetailStep - 0.00004d, observedRetailStep + 0.00004d);

        var released = controller.Step(new PlayerControlIntent(0, 0, false, false), Airborne);
        Assert.Equal(
            expectedTarget - Rac1RatchetMovementController.AirDecelerationPerTick,
            released.PlanarMagnitude,
            12);
    }

    [Fact]
    public void JumpAnticipation_UsesAirPlanarRecurrenceAfterNativeStateTransition()
    {
        var controller = new Rac1RatchetMovementController();
        controller.Step(new PlayerControlIntent(0, 0, true, true), Grounded);
        Assert.Equal(Rac1RatchetMovementPhase.JumpAnticipation, controller.Phase);

        var partial = new PlayerControlIntent(
            RightUnit(127),
            ForwardUnit(60),
            true,
            false);
        var first = controller.Step(partial, Grounded);
        var second = controller.Step(partial, Grounded);

        Assert.Equal(Rac1RatchetMovementController.AirAccelerationPerTick, first.PlanarMagnitude, 12);
        Assert.Equal(2d * Rac1RatchetMovementController.AirAccelerationPerTick, second.PlanarMagnitude, 12);
        Assert.Equal(Rac1RatchetMovementPhase.JumpAnticipation, second.Phase);
    }

    [Fact]
    public void PartialStickLedgeFall_UsesAirMagnitudeTargetWithoutTerrainTerms()
    {
        var controller = new Rac1RatchetMovementController();
        var input = IntentFromRaw(127, 237);

        for (int i = 0; i < 20; i++)
            controller.Step(input, Grounded, _ => -Math.PI / 2d);

        double groundStep = Math.Sqrt(
            (controller.PlanarX * controller.PlanarX) +
            (controller.PlanarY * controller.PlanarY));
        var firstFall = controller.Step(input, Airborne);
        var secondFall = controller.Step(input, Airborne);

        Assert.Equal(Rac1RatchetMovementController.WalkPlanarStep, groundStep, 12);
        Assert.Equal(Rac1RatchetMovementPhase.Falling, firstFall.Phase);
        Assert.Equal(-Rac1RatchetMovementController.FallGravityPerTick, firstFall.Vertical, 12);
        Assert.Equal(
            groundStep + Rac1RatchetMovementController.AirAccelerationPerTick,
            firstFall.PlanarMagnitude,
            12);
        Assert.Equal(
            firstFall.PlanarMagnitude + Rac1RatchetMovementController.AirAccelerationPerTick,
            secondFall.PlanarMagnitude,
            12);
    }

    [Fact]
    public void PartialStickTargetHeading_UsesConditionedComponents()
    {
        const byte rawX = 189;
        const byte rawY = 60;
        const double controlYaw = 1.2d;
        var conditioned = Rac1AnalogueInput.ConditionRawLeftStick(rawX, rawY);
        var yaw = new Rac1RatchetYawController();

        var step = yaw.Step(
            RightUnit(rawX),
            ForwardUnit(rawY),
            controlYaw,
            Rac1RatchetYawMode.GroundStartup);

        double expected = Rac1RatchetYawController.WrapPi(
            controlYaw - Math.Atan2(14d, 19d));
        Assert.True(conditioned.IsActive);
        Assert.Equal(expected, step.TargetYaw, 12);
    }

    [Fact]
    public void TargetHeading_DenseByteGridUsesConditionedDirection()
    {
        byte[] samples = [0, 32, 60, 94, 127, 160, 189, 220, 255];
        const double initialYaw = 0.37d;
        const double controlYaw = -1.14d;

        foreach (byte rawX in samples)
        {
            foreach (byte rawY in samples)
            {
                var conditioned = Rac1AnalogueInput.ConditionRawLeftStick(rawX, rawY);
                var yaw = new Rac1RatchetYawController(initialYaw);
                var step = yaw.Step(
                    RightUnit(rawX),
                    ForwardUnit(rawY),
                    controlYaw,
                    Rac1RatchetYawMode.GroundStartup);

                double expected = conditioned.IsActive
                    ? Rac1RatchetYawController.WrapPi(
                        controlYaw - Math.Atan2(conditioned.X, conditioned.Y))
                    : initialYaw;
                Assert.Equal(expected, step.TargetYaw, 12);
            }
        }
    }

    [Fact]
    public void InactiveDiagonal_DoesNotCreateFacingTarget()
    {
        const double initialYaw = 0.7d;
        var yaw = new Rac1RatchetYawController(initialYaw);

        var step = yaw.Step(
            RightUnit(188),
            ForwardUnit(188),
            -1.1d,
            Rac1RatchetYawMode.GroundStartup);

        Assert.Equal(initialYaw, step.TargetYaw, 12);
        Assert.Equal(initialYaw, step.CurrentYaw, 12);
        Assert.Equal(0d, step.YawVelocity, 12);
    }

    [Fact]
    public void CrouchUsesRadialActivationBoundaryForTurnState()
    {
        var controller = new Rac1RatchetMovementController();
        var inactive = controller.Step(
            IntentFromRaw(188, 188, crouch: true),
            Grounded);
        Assert.Equal(Rac1RatchetLocomotionState.Crouched, inactive.LocomotionState);

        var active = controller.Step(
            IntentFromRaw(189, 189, crouch: true),
            Grounded);
        Assert.Equal(Rac1RatchetLocomotionState.CrouchTurning, active.LocomotionState);
        Assert.Equal(Rac1RatchetYawMode.CrouchTurn, active.YawMode);
        Assert.Equal(0d, active.PlanarMagnitude, 12);
        Assert.Equal(0d, controller.TargetPlanarStep, 12);
    }

    private static PlayerControlIntent IntentFromRaw(
        byte rawX,
        byte rawY,
        bool crouch = false) =>
        new(
            RightUnit(rawX),
            ForwardUnit(rawY),
            false,
            false,
            CrouchHeld: crouch);

    private static double RightUnit(byte rawX) =>
        (rawX - Rac1AnalogueInput.NeutralRawByte) / Rac1AnalogueInput.HostAxisCountScale;

    private static double ForwardUnit(byte rawY) =>
        (Rac1AnalogueInput.NeutralRawByte - rawY) / Rac1AnalogueInput.HostAxisCountScale;
}
