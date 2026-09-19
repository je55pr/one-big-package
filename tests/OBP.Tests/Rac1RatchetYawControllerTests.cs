using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class Rac1RatchetYawControllerTests
{
    public static TheoryData<
        string,
        int,
        Rac1RatchetYawMode,
        double,
        double,
        double,
        double> FixedWitnessRows =>
        new()
        {
            // right_release_zero.json, native sequence 3.
            {
                "right_release_zero", 18, Rac1RatchetYawMode.GroundStartup,
                1.1946587562561035d, 0.05459320545196533d,
                2.6379005908966064d, 1.271214246749878d
            },
            {
                "right_release_zero", 25, Rac1RatchetYawMode.GroundStartup,
                1.9907184839248657d, 0.13392126560211182d,
                2.6237404346466064d, 2.1232750415802d
            },
            {
                "right_release_zero", 29, Rac1RatchetYawMode.GroundStartup,
                2.489868402481079d, 0.11504435539245605d,
                2.608574628829956d, 2.5956637859344482d
            },
            // right_release_zero.json, native sequence 4.
            {
                "right_release_zero", 35, Rac1RatchetYawMode.GroundRun,
                2.602226734161377d, -0.0008716583251953125d,
                2.576460599899292d, 2.6012797355651855d
            },
            {
                "right_release_zero", 44, Rac1RatchetYawMode.GroundRun,
                2.5877034664154053d, -0.0024788379669189453d,
                2.507429361343384d, 2.584954261779785d
            },
            // turn_to_crate.json, native crouch-turn sequence 14.
            {
                "turn_to_crate", 29, Rac1RatchetYawMode.CrouchTurn,
                1.1199871301651d, 0.005890250205993652d,
                2.6386094093322754d, 1.1285022497177124d
            },
            {
                "turn_to_crate", 35, Rac1RatchetYawMode.CrouchTurn,
                1.2037415504455566d, 0.018808364868164062d,
                2.6386094093322754d, 1.2241030931472778d
            },
            {
                "turn_to_crate", 45, Rac1RatchetYawMode.CrouchTurn,
                1.455000638961792d, 0.028481125831604004d,
                2.6386094093322754d, 1.4838552474975586d
            },
            // jump_air_forward.json, native air sequence 7.
            {
                "jump_air_forward", 35, Rac1RatchetYawMode.Air,
                -2.6157386302948d, 0.22286272048950195d,
                -2.0738065242767334d, -2.415771245956421d
            },
            {
                "jump_air_forward", 36, Rac1RatchetYawMode.Air,
                -2.415771245956421d, 0.1999673843383789d,
                -2.0737695693969727d, -2.242117166519165d
            },
            {
                "jump_air_forward", 37, Rac1RatchetYawMode.Air,
                -2.242117166519165d, 0.17365407943725586d,
                -2.073805332183838d, -2.096461534500122d
            },
            // forward_release_zero.json, sequence 3 at the verified ground cap.
            {
                "forward_release_zero", 30, Rac1RatchetYawMode.GroundStartup,
                -3.023371934890747d, 0.16580588022340947d,
                -2.0737948417663574d, -2.8575656414031982d
            },
        };

    [Fact]
    public void MovementTarget_UsesRecoveredStickControlHeadingRule()
    {
        Assert.Equal(
            0d,
            Rac1RatchetYawController.BuildMovementTarget(0d, 1d, 0d),
            12);
        Assert.Equal(
            -Math.PI / 2d,
            Rac1RatchetYawController.BuildMovementTarget(1d, 0d, 0d),
            12);
        Assert.Equal(
            Math.PI / 2d,
            Rac1RatchetYawController.BuildMovementTarget(-1d, 0d, 0d),
            12);
        Assert.Equal(
            -Math.PI / 4d,
            Rac1RatchetYawController.BuildMovementTarget(1d, 1d, 0d),
            12);
    }

    [Theory]
    [MemberData(nameof(FixedWitnessRows))]
    public void FixedNtscuWitnessRows_ReplayRecoveredRecurrence(
        string source,
        int row,
        Rac1RatchetYawMode mode,
        double currentYaw,
        double previousVelocity,
        double targetYaw,
        double expectedYaw)
    {
        var step = Rac1RatchetYawController.AdvanceRecurrence(
            currentYaw,
            previousVelocity,
            targetYaw,
            mode);

        double yawError = Math.Abs(Rac1RatchetYawController.WrapPi(step.CurrentYaw - expectedYaw));
        double expectedVelocity = Rac1RatchetYawController.WrapPi(expectedYaw - currentYaw);
        Assert.True(
            yawError <= 0.000002d,
            $"{source} row {row}: yaw error {yawError:R}");
        Assert.InRange(
            Math.Abs(step.YawVelocity - expectedVelocity),
            0d,
            0.000002d);
    }

    [Fact]
    public void GroundStartupTurn_WrapsAcrossPiByShortestError()
    {
        double initial = Math.PI - 0.05d;
        double target = -Math.PI + 0.05d;
        var controller = new Rac1RatchetYawController(initial);

        var step = controller.Step(
            0d,
            1d,
            target,
            Rac1RatchetYawMode.GroundStartup);

        Assert.Equal(target, step.TargetYaw, 12);
        Assert.Equal(
            Rac1RatchetYawController.GroundStartupErrorGain * 0.1d,
            Rac1RatchetYawController.WrapPi(step.CurrentYaw - initial),
            12);
        Assert.Equal(Rac1RatchetYawMode.GroundStartup, step.Mode);
    }

    [Fact]
    public void GroundStartupAndRun_UseDifferentRecoveredRecurrences()
    {
        const double target = 1d;
        var startup = Rac1RatchetYawController.AdvanceRecurrence(
            currentYaw: 0d,
            yawVelocity: 0.05d,
            targetYaw: target,
            Rac1RatchetYawMode.GroundStartup);
        var run = Rac1RatchetYawController.AdvanceRecurrence(
            currentYaw: 0d,
            yawVelocity: 0.05d,
            targetYaw: target,
            Rac1RatchetYawMode.GroundRun);

        Assert.Equal(
            (0.9d * 0.05d) + (0.019d * target),
            startup.YawVelocity,
            12);
        Assert.Equal(
            0.05d +
            (Rac1RatchetYawController.GroundRunErrorGain * target) -
            (Rac1RatchetYawController.GroundRunVelocityDamping * 0.05d),
            run.YawVelocity,
            12);
        Assert.True(startup.YawVelocity > run.YawVelocity);
    }

    [Fact]
    public void CrouchTurn_UsesRecoveredSlowTurnRecurrence()
    {
        var step = Rac1RatchetYawController.AdvanceRecurrence(
            currentYaw: 0d,
            yawVelocity: 0.01d,
            targetYaw: 1d,
            Rac1RatchetYawMode.CrouchTurn);

        Assert.Equal((0.93d * 0.01d) + 0.002d, step.YawVelocity, 12);
        Assert.Equal(step.YawVelocity, step.CurrentYaw, 12);
    }

    [Fact]
    public void GroundCap_ReplaysFixedRetailWitness()
    {
        var step = Rac1RatchetYawController.AdvanceRecurrence(
            currentYaw: -3.023371934890747d,
            yawVelocity: 0.16580588022340947d,
            targetYaw: -2.0737948417663574d,
            Rac1RatchetYawMode.GroundStartup);

        double observedVelocity = Rac1RatchetYawController.WrapPi(
            -2.8575656414031982d - -3.023371934890747d);
        Assert.Equal(Rac1RatchetYawController.GroundMaximumStep, step.YawVelocity, 12);
        Assert.InRange(
            Math.Abs(step.YawVelocity - observedVelocity),
            0d,
            0.00000002d);
    }

    [Fact]
    public void AirMaximumStep_MatchesFixedRetailWitnessEnvelope()
    {
        const double observed = 0.2501637935638428d;

        Assert.InRange(
            Math.Abs(Rac1RatchetYawController.AirMaximumStep - observed),
            0d,
            0.0000001d);

        var step = Rac1RatchetYawController.AdvanceRecurrence(
            currentYaw: 0d,
            yawVelocity: 0.4d,
            targetYaw: Math.PI - 0.01d,
            Rac1RatchetYawMode.Air);
        Assert.Equal(Rac1RatchetYawController.AirMaximumStep, step.YawVelocity, 12);
    }

    [Theory]
    [InlineData(
        Rac1RatchetYawMode.GroundRun,
        -2.1160213947296143d,
        0.12278079986572266d,
        -2.0741095542907715d)]
    [InlineData(
        Rac1RatchetYawMode.Air,
        -2.096461534500122d,
        0.14565563201904297d,
        -2.07377028465271d)]
    public void RetailOvershootWitness_HitsTargetAndClearsVelocity(
        Rac1RatchetYawMode mode,
        double currentYaw,
        double previousVelocity,
        double targetYaw)
    {
        var step = Rac1RatchetYawController.AdvanceRecurrence(
            currentYaw,
            previousVelocity,
            targetYaw,
            mode);

        Assert.Equal(targetYaw, step.CurrentYaw, 12);
        Assert.Equal(0d, step.YawVelocity, 12);
    }

    [Fact]
    public void NearTargetWithoutOvershoot_DoesNotUseUnsupportedEarlySnap()
    {
        var step = Rac1RatchetYawController.AdvanceRecurrence(
            currentYaw: 0d,
            yawVelocity: 0d,
            targetYaw: 0.001d,
            Rac1RatchetYawMode.GroundRun);

        Assert.NotEqual(0.001d, step.CurrentYaw);
        Assert.Equal(Rac1RatchetYawController.GroundRunErrorGain * 0.001d, step.YawVelocity, 12);
        Assert.Equal(step.YawVelocity, step.CurrentYaw, 12);
    }

    [Fact]
    public void ZeroInput_RetainsCurrentYawAndClearsTurnVelocity()
    {
        var controller = new Rac1RatchetYawController();
        controller.Step(
            0d,
            1d,
            1d + (Math.PI / 2d),
            Rac1RatchetYawMode.GroundStartup);
        double before = controller.CurrentYaw;

        var step = controller.Step(
            0d,
            0d,
            -2d,
            Rac1RatchetYawMode.GroundRun);

        Assert.Equal(-2d, step.ControlYaw, 12);
        Assert.Equal(before, step.TargetYaw, 12);
        Assert.Equal(before, step.CurrentYaw, 12);
        Assert.Equal(0d, step.YawVelocity, 12);
    }

    [Fact]
    public void GroundRun_ConvergesExactlyToFixedTarget()
    {
        var controller = new Rac1RatchetYawController();
        double target = 1.2d;
        for (int i = 0; i < 240; i++)
        {
            controller.Step(
                0d,
                1d,
                target,
                Rac1RatchetYawMode.GroundRun);
        }

        Assert.Equal(target, controller.CurrentYaw, 12);
        Assert.Equal(0d, controller.YawVelocity, 12);
    }

    [Fact]
    public void CameraRotationAlone_DoesNotRedirectWrenchFacing()
    {
        var yaw = new Rac1RatchetYawController(0.6627015d);
        var wrench = new Rac1WrenchCombatController();
        var before = wrench.ResolveFirstSwingFacing(yaw.CurrentYaw);

        yaw.Step(
            0d,
            0d,
            -2.4d,
            Rac1RatchetYawMode.GroundRun);
        var after = wrench.ResolveFirstSwingFacing(yaw.CurrentYaw);

        Assert.Equal(before.X, after.X, 12);
        Assert.Equal(before.Y, after.Y, 12);
        Assert.Equal(0.6627015d, yaw.CurrentYaw, 12);
        Assert.Equal(-2.4d, yaw.ControlYaw, 12);
    }

    [Fact]
    public void GroundedCrouchInput_UsesCrouchTurnWithoutCreatingTranslation()
    {
        var movement = new Rac1RatchetMovementController();
        var yaw = new Rac1RatchetYawController();
        var crouched = movement.Step(
            new PlayerControlIntent(1d, 0d, false, false, CrouchHeld: true),
            new PlayerContactFacts(true));
        var turn = yaw.Step(
            1d,
            0d,
            0d,
            crouched.YawMode);

        Assert.Equal(0d, crouched.PlanarMagnitude, 12);
        Assert.Equal(Rac1RatchetYawMode.CrouchTurn, crouched.YawMode);
        Assert.NotEqual(0d, turn.CurrentYaw);
        Assert.Equal(Rac1RatchetYawMode.CrouchTurn, turn.Mode);
    }
}
