using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class Rac1RatchetMovementControllerTests
{
    private static readonly PlayerContactFacts Grounded = new(true);
    private static readonly PlayerContactFacts Airborne = new(false);
    private static readonly PlayerPlanarBasis RetailTargetBasis = new(0d, -1d, 1d, 0d);

    public static TheoryData<double, double> RunTurnInputs =>
        new()
        {
            { 1d, 1d },
            { 1d, 0d },
            { 1d, -1d },
            { 0d, -1d },
            { -1d, -1d },
            { -1d, 0d },
            { -1d, 1d },
        };

    private static Rac1RatchetMovementController.StepResult StepCoupledGround(
        Rac1RatchetMovementController movement,
        Rac1RatchetYawController yaw,
        double inputX,
        double inputY)
    {
        var input = new PlayerControlIntent(
            inputX,
            inputY,
            false,
            false,
            PlanarBasis: RetailTargetBasis,
            NativePlanarBasis: PlayerPlanarBasis.Identity);
        return movement.Step(
            input,
            Grounded,
            mode => yaw.Step(inputX, inputY, 0d, mode).CurrentYaw);
    }

    private static void AssertPlanarTracksYaw(
        Rac1RatchetMovementController.StepResult step,
        Rac1RatchetYawController yaw)
    {
        Assert.Equal(System.Math.Cos(yaw.CurrentYaw) * step.PlanarMagnitude, step.PlanarX, 12);
        Assert.Equal(System.Math.Sin(yaw.CurrentYaw) * step.PlanarMagnitude, step.PlanarY, 12);
    }

    [Fact]
    public void GroundRun_AcceleratesToRetailPlateau()
    {
        var controller = new Rac1RatchetMovementController();
        var input = new PlayerControlIntent(0, 1, false, false);

        var first = controller.Step(input, Grounded, _ => Math.PI / 2d);
        Assert.Equal(Rac1RatchetMovementController.GroundAccelerationPerTick, first.PlanarMagnitude, 12);

        Rac1RatchetMovementController.StepResult step = first;
        for (int i = 1; i < 80; i++)
            step = controller.Step(input, Grounded, _ => Math.PI / 2d);

        Assert.Equal(Rac1RatchetMovementController.MaximumPlanarStep, step.PlanarMagnitude, 12);
        Assert.Equal(0.09500919d, step.PlanarMagnitude, 8);
    }

    [Fact]
    public void GroundWalkRelease_ReplaysRetainedNonUniformStop()
    {
        var controller = new Rac1RatchetMovementController();
        var walk = new PlayerControlIntent(0, 0.75, false, false);
        for (int i = 0; i < 12; i++)
            controller.Step(walk, Grounded, _ => Math.PI / 2d);

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
            var step = controller.Step(release, Grounded, _ => Math.PI / 2d);
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
            controller.Step(run, Grounded, _ => Math.PI / 2d);

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
            var handoffStep = controller.Step(release, Grounded, _ => Math.PI / 2d);
            Assert.Equal(expectedStep, handoffStep.PlanarMagnitude, 12);
        }

        var sustained = controller.Step(release, Grounded, _ => Math.PI / 2d);
        Assert.Equal(
            handoff[^1] - Rac1RatchetMovementController.GroundDecelerationPerTick,
            sustained.PlanarMagnitude,
            12);

        Rac1RatchetMovementController.StepResult step = sustained;
        for (int i = 0; i < 40; i++)
            step = controller.Step(release, Grounded, _ => Math.PI / 2d);

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
            var step = controller.Step(new PlayerControlIntent(0, 0, true, tick == 0), Grounded, _ => Math.PI / 2d);
            Assert.Equal(0d, step.Vertical);
        }

        var launch = controller.Step(new PlayerControlIntent(0, 0, true, false), Grounded, _ => Math.PI / 2d);
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
    public void LeavingOrdinaryGround_UsesRetailAdhesionThenEdgeGravityRecurrence()
    {
        var controller = new Rac1RatchetMovementController();
        var input = new PlayerControlIntent(0, 0, false, false);

        var airborne = Rac1PlayerContactResult.StaticWorld(false);
        var firstUnsupportedControllerTick = controller.Step(input, airborne);
        Assert.Equal(Rac1RatchetMovementPhase.Falling, firstUnsupportedControllerTick.Phase);
        Assert.Equal(
            -(Rac1OrdinaryGroundContactMotion.GroundDownwardRequestPerTick +
              Rac1OrdinaryGroundContactMotion.EdgeFallAccelerationPerTick),
            firstUnsupportedControllerTick.Vertical,
            12);

        var next = controller.Step(input, airborne);
        Assert.Equal(
            firstUnsupportedControllerTick.Vertical -
            Rac1OrdinaryGroundContactMotion.EdgeFallAccelerationPerTick,
            next.Vertical,
            12);
    }

    [Fact]
    public void GenericContactFallback_DoesNotImportRac1EdgeContactRecurrence()
    {
        var controller = new Rac1RatchetMovementController();
        var step = controller.Step(
            new PlayerControlIntent(0, 0, false, false),
            Airborne);

        Assert.Equal(Rac1RatchetMovementPhase.Falling, step.Phase);
        Assert.Equal(-Rac1RatchetMovementController.FallGravityPerTick, step.Vertical, 12);
    }

    [Fact]
    public void OrdinarySupportContactMetric_PinsRetailTwoCentimeterAdmissionGate()
    {
        Assert.Equal(
            0.019999999552965164d,
            Rac1OrdinaryGroundContactMotion.OrdinarySupportContactMetricLimit,
            15);
    }

    [Fact]
    public void OrdinarySupportAngle_PinsRetailFiftyDegreeAdmissionGate()
    {
        Assert.Equal(
            0.8726646304130554d,
            Rac1OrdinaryGroundContactMotion.OrdinarySupportMaxAngleRadians,
            15);
        Assert.Equal(
            50d,
            Rac1OrdinaryGroundContactMotion.OrdinarySupportMaxAngleRadians * 180d / Math.PI,
            5);
    }

    [Fact]
    public void OrdinaryGroundTerrain_UsesDirectionalSlopeWithoutCrossSlopeSteering()
    {
        const double yaw = -2.0740272998809814d;
        const double runCap = 0.09500919d;
        var movement = new Rac1RatchetMovementController.StepResult(
            Math.Cos(yaw) * runCap,
            Math.Sin(yaw) * runCap,
            0d,
            Rac1RatchetMovementPhase.Grounded,
            Rac1RatchetLocomotionState.Moving,
            Rac1RatchetYawMode.GroundRun);

        var projected = Rac1OrdinaryGroundContactMotion.ResolvePreContactStep(
            movement,
            Grounded,
            normalPlanarX: 0.07734177119116313d,
            normalPlanarY: 0.3387449668450168d,
            normalUp: 0.9376940321161173d);

        Assert.Equal(-0.04315774142742157d, projected.PlanarX, 5);
        Assert.Equal(-0.07839659601449966d, projected.PlanarY, 5);
        Assert.Equal(0.016880685463547707d, projected.Vertical, 5);
        Assert.Equal(
            Math.Atan2(movement.PlanarY, movement.PlanarX),
            Math.Atan2(projected.PlanarY, projected.PlanarX),
            12);
        Assert.Equal(
            runCap,
            Math.Sqrt(
                (projected.PlanarX * projected.PlanarX) +
                (projected.PlanarY * projected.PlanarY) +
                Math.Pow(
                    projected.Vertical +
                    Rac1OrdinaryGroundContactMotion.GroundDownwardRequestPerTick,
                    2)),
            8);
    }

    [Fact]
    public void SupportedOrdinaryGround_AddsRetailPreContactAdhesionOnlyOutsideJumpStates()
    {
        var controller = new Rac1RatchetMovementController();
        var idle = controller.Step(new PlayerControlIntent(0, 0, false, false), Grounded);
        Assert.Equal(0d, idle.Vertical, 12);
        Assert.Equal(
            -Rac1OrdinaryGroundContactMotion.GroundDownwardRequestPerTick,
            Rac1OrdinaryGroundContactMotion.ResolvePreContactVertical(idle, Grounded),
            12);

        var anticipation = controller.Step(new PlayerControlIntent(0, 0, true, true), Grounded);
        Assert.Equal(Rac1RatchetMovementPhase.JumpAnticipation, anticipation.Phase);
        Assert.Equal(
            anticipation.Vertical,
            Rac1OrdinaryGroundContactMotion.ResolvePreContactVertical(anticipation, Grounded),
            12);
    }

    [Fact]
    public void Landing_ClearsVerticalMotionButPreservesPlanarMotion()
    {
        var controller = new Rac1RatchetMovementController();
        controller.Step(new PlayerControlIntent(0, 1, true, true), Grounded, _ => Math.PI / 2d);
        for (int i = 1; i < Rac1RatchetMovementController.JumpAnticipationTicks; i++)
            controller.Step(new PlayerControlIntent(0, 1, true, false), Grounded, _ => Math.PI / 2d);

        var airborne = controller.Step(new PlayerControlIntent(0, 1, false, false), Airborne);
        Assert.NotEqual(0d, airborne.Vertical);
        double planar = airborne.PlanarMagnitude;

        var landed = controller.Step(new PlayerControlIntent(0, 1, false, false), Grounded, _ => Math.PI / 2d);
        Assert.Equal(Rac1RatchetMovementPhase.Grounded, landed.Phase);
        Assert.Equal(0d, landed.Vertical);
        Assert.True(landed.PlanarMagnitude >= planar);
    }
    [Fact]
    public void GroundedCrouch_SuppressesNewTranslationAndJump()
    {
        var controller = new Rac1RatchetMovementController();
        var crouch = new PlayerControlIntent(1, 0, true, true, CrouchHeld: true);

        var step = controller.Step(crouch, Grounded, _ => Math.PI / 2d);

        Assert.Equal(0d, step.PlanarMagnitude, 12);
        Assert.Equal(0d, step.Vertical, 12);
        Assert.Equal(Rac1RatchetMovementPhase.Grounded, step.Phase);
    }

    [Fact]
    public void Reset_ClearsMomentumAndJumpStateForRespawn()
    {
        var controller = new Rac1RatchetMovementController();
        var runJump = new PlayerControlIntent(0, 1, true, true);
        controller.Step(runJump, Grounded, _ => Math.PI / 2d);
        for (int i = 1; i < Rac1RatchetMovementController.JumpAnticipationTicks; i++)
            controller.Step(new PlayerControlIntent(0, 1, true, false), Grounded, _ => Math.PI / 2d);
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
            controller.Step(run, Grounded, _ => Math.PI / 2d);

        double before = controller.PlanarY;
        var crouched = controller.Step(new PlayerControlIntent(0, 1, false, false, CrouchHeld: true), Grounded, _ => Math.PI / 2d);

        Assert.Equal(before - Rac1RatchetMovementController.CrouchDecelerationPerTick, crouched.PlanarY, 9);
        Assert.True(crouched.PlanarMagnitude < before);
    }

    [Theory]
    [InlineData(0, Rac1SurfaceInteractionKind.ShallowWaterWade)]
    [InlineData(3, Rac1SurfaceInteractionKind.MudSink)]
    [InlineData(7, Rac1SurfaceInteractionKind.IceSlide)]
    public void OrdinaryControllerRejectsRecoveredAlternateStaticSurfacesBeforeMutation(
        int rawFaceType,
        Rac1SurfaceInteractionKind expectedInteraction)
    {
        var controller = new Rac1RatchetMovementController();
        var contact = Rac1PlayerContactResult.StaticWorld(
            isGrounded: true,
            rawFaceType: rawFaceType);

        Assert.Equal(expectedInteraction, contact.SurfaceInteraction);
        Assert.Throws<NotSupportedException>(() =>
            controller.Step(
                new PlayerControlIntent(0d, 1d, false, false),
                contact,
                _ => 0d));
        Assert.Equal(0d, controller.PlanarMagnitude);
        Assert.Equal(0d, controller.VerticalStep);
        Assert.Equal(Rac1RatchetMovementPhase.Grounded, controller.Phase);
    }

    [Fact]
    public void OrdinaryControllerRejectsRecoveredMagnebootSupportBeforeMutation()
    {
        var controller = new Rac1RatchetMovementController();
        var support = new Rac1MobyRuntimeKey(
            Rac1PlayerContactResult.MagnebootSupportMobyClass,
            12);
        var contact = new Rac1PlayerContactResult(
            IsGrounded: true,
            HitCeiling: false,
            Face: Rac1CollisionFaceSemantics.Decode(2),
            ContactedMoby: support,
            CurrentDynamicContact: support,
            PersistentSupportMoby: support,
            SupportAnchor: new Rac1SupportAnchorState(1u, true),
            SupportCarry: Rac1SupportCarry.None,
            Conveyor: Rac1ConveyorTransfer.None);

        Assert.Equal(
            Rac1SurfaceInteractionKind.MagnebootSupport,
            contact.SurfaceInteraction);
        Assert.Throws<NotSupportedException>(() =>
            controller.Step(
                new PlayerControlIntent(0d, 1d, false, false),
                contact,
                _ => 0d));
        Assert.Equal(0d, controller.PlanarMagnitude);
        Assert.Equal(0d, controller.VerticalStep);
        Assert.Equal(Rac1RatchetMovementPhase.Grounded, controller.Phase);
    }

    [Fact]
    public void GroundedActiveInput_RequiresSameUpdateFacingResolution()
    {
        var controller = new Rac1RatchetMovementController();

        Assert.Throws<InvalidOperationException>(() =>
            controller.Step(new PlayerControlIntent(0d, 1d, false, false), Grounded));
    }

    [Theory]
    [MemberData(nameof(RunTurnInputs))]
    public void GroundRun_TurnsRotateWithSameUpdateFacingWithoutVectorSpeedLoss(
        double turnX,
        double turnY)
    {
        var movement = new Rac1RatchetMovementController();
        var yaw = new Rac1RatchetYawController();
        for (int i = 0; i < 80; i++)
            StepCoupledGround(movement, yaw, 0d, 1d);

        var turned = StepCoupledGround(movement, yaw, turnX, turnY);

        Assert.Equal(Rac1RatchetMovementController.MaximumPlanarStep, turned.PlanarMagnitude, 12);
        AssertPlanarTracksYaw(turned, yaw);
    }

    [Fact]
    public void GroundWalk_LeftRightReversalKeepsPlateauAndTracksFacing()
    {
        var movement = new Rac1RatchetMovementController();
        var yaw = new Rac1RatchetYawController();
        for (int i = 0; i < 16; i++)
            StepCoupledGround(movement, yaw, 0.75d, 0d);

        var reversed = StepCoupledGround(movement, yaw, -0.75d, 0d);

        Assert.Equal(Rac1RatchetMovementController.WalkPlanarStep, reversed.PlanarMagnitude, 12);
        AssertPlanarTracksYaw(reversed, yaw);
    }

    [Fact]
    public void RunningJump_ReplaysRetailAirTimeAndDistance()
    {
        var controller = new Rac1RatchetMovementController();
        var run = new PlayerControlIntent(0, 1, false, false);
        for (int i = 0; i < 80; i++)
            controller.Step(run, Grounded, _ => Math.PI / 2d);

        Rac1RatchetMovementController.StepResult step = default;
        for (int tick = 0; tick < Rac1RatchetMovementController.JumpAnticipationTicks; tick++)
        {
            bool held = tick < 6;
            step = controller.Step(new PlayerControlIntent(0, 1, held, tick == 0), Grounded, _ => Math.PI / 2d);
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
