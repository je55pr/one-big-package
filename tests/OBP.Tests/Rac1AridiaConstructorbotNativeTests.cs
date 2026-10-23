using System.Buffers.Binary;
using System.Numerics;
using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1AridiaConstructorbotNativeTests
{
    private static byte[] Pvar(int mode)
    {
        byte[] pvar = new byte[576];
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x194), mode);
        return pvar;
    }

    [Fact]
    public void NativeClipRecordWaypointIndexAndFullThreeDimensionalDistanceAreBounded()
    {
        var p = Pvar(3);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0x188), 12);
        Assert.Equal(12, Rac1AridiaConstructorbotNative.ReadWaypointRecordIndex(p, 86));
        Assert.Throws<InvalidDataException>(() =>
            Rac1AridiaConstructorbotNative.ReadWaypointRecordIndex(p, 12));
        Assert.Throws<InvalidDataException>(() =>
            Rac1AridiaConstructorbotNative.ReadWaypointRecordIndex(p, 0));
        Assert.Throws<InvalidDataException>(() =>
            Rac1AridiaConstructorbotNative.ReadWaypointRecordIndex(new byte[0x188], 86));
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0x188), -1);
        Assert.Throws<InvalidDataException>(() =>
            Rac1AridiaConstructorbotNative.ReadWaypointRecordIndex(p, 86));

        Vector3 centre = new(100, 200, 50);
        Assert.True(Rac1AridiaConstructorbotNative.NativeWaypointActivation(
            centre + new Vector3(3, 4, 0), centre, 5.01f));
        Assert.False(Rac1AridiaConstructorbotNative.NativeWaypointActivation(
            centre + new Vector3(3, 4, 0), centre, 5f));
        Assert.False(Rac1AridiaConstructorbotNative.NativeWaypointActivation(
            centre + new Vector3(0, 0, 20), centre, 20f));
        Assert.True(Rac1AridiaConstructorbotNative.NativeWaypointActivation(
            centre, centre, .01f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.NativeWaypointActivation(centre, centre, -1f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.NativeWaypointActivation(
                new Vector3(float.NaN, 0, 0), centre, 20f));
    }

    [Theory]
    [InlineData(0, 7, 0)]
    [InlineData(1, 10, 0)]
    [InlineData(2, 8, 2)]
    [InlineData(3, 9, 15)]
    public void NativeInitUsesAuthoredModeRatherThanUniversalFlamethrower(int mode, int state, int sequence)
    {
        var init = Rac1AridiaConstructorbotNative.Initialize(Pvar(mode));
        Assert.Equal(new(state, sequence), init);
        Assert.Equal(mode, Rac1AridiaConstructorbotNative.ReadInitialMode(Pvar(mode)));
        var session = new Rac1AridiaEnemySession();
        session.Register(55, Rac1AridiaEnemies.Constructorbot, constructorbotStartupSequence: sequence);
        var noPlayer = new Rac1AridiaEnemyFacts(Vector3.Zero, new(100, 0, 0), false, false);
        for (int i = 0; i < 90; i++)
        {
            var frame = session.Step(55, noPlayer);
            Assert.Equal(Rac1AridiaEnemyPhase.Guarding, frame.Phase);
            Assert.Equal(sequence, frame.Sequence);
            Assert.False(frame.DamagePlayer);
            Assert.False(frame.FlameActive);
        }
    }

    [Fact]
    public void ModeReaderRejectsMissingAndUnrecognizedSourcePvar()
    {
        Assert.Throws<InvalidDataException>(() => Rac1AridiaConstructorbotNative.Initialize(new byte[0x194]));
        Assert.Throws<InvalidDataException>(() => Rac1AridiaConstructorbotNative.Initialize(Pvar(-1)));
        Assert.Throws<InvalidDataException>(() => Rac1AridiaConstructorbotNative.Initialize(Pvar(4)));
    }

    [Fact]
    public void StateSevenUsesAllFourNativeGatesBeforeJointContact()
    {
        byte[] pvar = Pvar(0);
        BinaryPrimitives.WriteSingleLittleEndian(pvar.AsSpan(0x180), 18f);
        Assert.Equal(18f, Rac1AridiaConstructorbotNative.ReadEngagementRadius(pvar));
        bool Accept(float target = 17.99f, float vertical = 3.99f,
            float anchor = .99f, float yaw = .08f) =>
            Rac1AridiaConstructorbotNative.State7AdmitsJointContact(
                target, vertical, anchor, yaw, 18f);

        Assert.True(Accept());
        Assert.False(Accept(target: 18f));
        Assert.False(Accept(vertical: 4f));
        Assert.False(Accept(vertical: -4f));
        Assert.False(Accept(anchor: 1f));
        Assert.False(Accept(yaw: MathF.PI / 36f));
        Assert.False(Accept(yaw: -.2f));
        Assert.False(Accept(target: float.NaN));
        Assert.False(Accept(yaw: float.PositiveInfinity));
        Assert.Throws<InvalidDataException>(() =>
            Rac1AridiaConstructorbotNative.ReadEngagementRadius(new byte[0x180]));
        BinaryPrimitives.WriteSingleLittleEndian(pvar.AsSpan(0x180), float.NaN);
        Assert.Throws<InvalidDataException>(() =>
            Rac1AridiaConstructorbotNative.ReadEngagementRadius(pvar));
    }

    [Fact]
    public void SourceParticle15EmitsOnlyOnNativeStateEightFourTickGateAndActiveMoby()
    {
        Assert.Equal(15, Rac1AridiaConstructorbotNative.SourceEmitterParticleId);
        Assert.Equal(3, Rac1AridiaConstructorbotNative.NativeEmitterCadenceMask);
        int emitted = 0;
        for (uint tick = 0; tick < 64; tick++)
        {
            bool emission = Rac1AridiaConstructorbotNative.ShouldEmitSourceParticle(
                Rac1AridiaConstructorbotNative.ParticleModeState, tick, mobyActive: true);
            if (emission) emitted++;
            Assert.Equal((tick & 3) == 0, emission);
            Assert.False(Rac1AridiaConstructorbotNative.ShouldEmitSourceParticle(
                Rac1AridiaConstructorbotNative.ParticleAlternateState, tick, true));
            Assert.False(Rac1AridiaConstructorbotNative.ShouldEmitSourceParticle(
                Rac1AridiaConstructorbotNative.ParticleModeState, tick, false));
            Assert.False(Rac1AridiaConstructorbotNative.ShouldEmitSourceParticle(
                Rac1AridiaConstructorbotNative.JointContactState, tick, true));
        }
        Assert.Equal(16, emitted);
        Assert.True(Rac1AridiaConstructorbotNative.ShouldEmitSourceParticle(
            8, uint.MaxValue - 3, true));
    }

    [Theory]
    [InlineData(8, 2, 4)]
    [InlineData(9, 2, 16)]
    public void NativeVolumeOrWaypointActivationRequiresAffirmativeEvidence(
        int start, int activatedState, int activatedSequence)
    {
        Assert.Equal(start, Rac1AridiaConstructorbotNative.StepActivation(start, null).NativeState);
        Assert.Equal(start, Rac1AridiaConstructorbotNative.StepActivation(start, false).NativeState);
        Assert.Equal(new(activatedState, activatedSequence),
            Rac1AridiaConstructorbotNative.StepActivation(start, true));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.StepActivation(7, true));
    }

    [Fact]
    public void WalkerVerticalProbeClampsNativeGroundAndRejectsBadExtents()
    {
        Assert.Equal((12.5f, 11.5f),
            Rac1AridiaConstructorbotNative.WalkerProbeHeights(12, .5f, .5f));
        Assert.Equal((.7f, 0f),
            Rac1AridiaConstructorbotNative.WalkerProbeHeights(.2f, .5f, .5f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.WalkerProbeHeights(12, -.1f, .5f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.WalkerProbeHeights(float.NaN, .5f, .5f));
    }

    [Theory]
    [InlineData(true, 0, 0)]
    [InlineData(false, 0, 0x22)]
    [InlineData(false, 8, 8)]
    [InlineData(true, 8, 0)]
    public void WalkerMissingFloorProbeSelectsActualRecordLedgeMode(
        bool hasSupport, int recordFlags, int expected)
    {
        Assert.Equal(expected, Rac1AridiaConstructorbotNative.WalkerInitialProbeFlags(
            hasSupport, recordFlags));
    }

    [Fact]
    public void WalkerRejectsFloorHeightOutsideOriginalAsymmetricBounds()
    {
        Assert.False(Rac1AridiaConstructorbotNative.WalkerFloorHeightRejectsStep(
            10, 9.5f, .5f, .5f));
        Assert.False(Rac1AridiaConstructorbotNative.WalkerFloorHeightRejectsStep(
            10, 10.5f, .5f, .5f));
        Assert.True(Rac1AridiaConstructorbotNative.WalkerFloorHeightRejectsStep(
            10, 9.49f, .5f, .5f));
        Assert.True(Rac1AridiaConstructorbotNative.WalkerFloorHeightRejectsStep(
            10, 10.51f, .5f, .5f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.WalkerFloorHeightRejectsStep(
                10, 10, float.NaN, .5f));
    }

    [Fact]
    public void WalkerLookaheadOnlyRunsAboveNativeSlopeAndSpeedThresholds()
    {
        Assert.Equal(0, Rac1AridiaConstructorbotNative.WalkerLookaheadFlags(
            1f, .1f, null, 0, 2, 0, 2, 0));
        Assert.Equal(0, Rac1AridiaConstructorbotNative.WalkerLookaheadFlags(
            0f, 2f, null, 0, 2, 0, 2, 0));
        Assert.Equal(0, Rac1AridiaConstructorbotNative.WalkerLookaheadFlags(
            MathF.Sin(.5f), 2f, null, 0, 2, 0, 2, 0));
        Assert.Throws<InvalidDataException>(() =>
            Rac1AridiaConstructorbotNative.WalkerLookaheadFlags(
                1f, 2f, null, 0, 2, 0, 2, 0));
    }

    [Fact]
    public void WalkerLookaheadBlocksMissingGroundAndSecondSteepHit()
    {
        // Native class612 behavior flags +0x38 = 0 cannot use bit8 ledge mode.
        Assert.Equal(0x22, Rac1AridiaConstructorbotNative.WalkerLookaheadFlags(
            1, .11f, false, 0, 10, 0, 10, 0));
        Assert.Equal(0, Rac1AridiaConstructorbotNative.WalkerLookaheadFlags(
            1, .11f, true, 0, 10, 10, 10, 0));
        Assert.Equal(0x22, Rac1AridiaConstructorbotNative.WalkerLookaheadFlags(
            1, .11f, true, 1, 9.9f, 9.9f, 10, 0));
        // Second normal exactly at 0.5-radian threshold still blocks,
        // although first-hit admission requires strictly greater than 0.5.
        Assert.Equal(0x22, Rac1AridiaConstructorbotNative.WalkerLookaheadFlags(
            1, .11f, true, MathF.Sin(.5f), 10, 10, 10, 0));
    }

    [Fact]
    public void WalkerLedgeModeRequiresOriginalHitHeightChecks()
    {
        int ledgeMode = 8;
        Assert.Equal(8, Rac1AridiaConstructorbotNative.WalkerLookaheadFlags(
            1, 1, false, 0, 9.9f, 0, 10, ledgeMode));
        Assert.Equal(0x22, Rac1AridiaConstructorbotNative.WalkerLookaheadFlags(
            1, 1, false, 0, 10.1f, 0, 10, ledgeMode));
        Assert.Equal(8, Rac1AridiaConstructorbotNative.WalkerLookaheadFlags(
            1, 1, true, 1, 9.9f, 9.99f, 10, ledgeMode));
        Assert.Equal(0x22, Rac1AridiaConstructorbotNative.WalkerLookaheadFlags(
            1, 1, true, 1, 9.9f, 10.05f, 10, ledgeMode));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.WalkerLookaheadFlags(
                1, 1, true, float.NaN, 10, 10, 10, ledgeMode));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.WalkerLookaheadFlags(
                1, 1, true, 1, 10, 10, 10, 0x400));
    }

    [Fact]
    public void WalkerSteepOrFlatSurfaceLookaheadUsesNativeThresholdAndSpeed()
    {
        Assert.True(Rac1AridiaConstructorbotNative.WalkerNeedsLookahead(
            1, .10001f));
        Assert.False(Rac1AridiaConstructorbotNative.WalkerNeedsLookahead(
            1, .1f));
        Assert.False(Rac1AridiaConstructorbotNative.WalkerNeedsLookahead(
            0, 2));
        Assert.False(Rac1AridiaConstructorbotNative.WalkerNeedsLookahead(
            MathF.Sin(.5f), 2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.WalkerNeedsLookahead(2, .2f));
    }

    [Fact]
    public void WalkerFlagsPreserveNativeBitmaskAndNeverInventCollision()
    {
        Assert.Equal(4, Rac1AridiaConstructorbotNative.WalkerFlags(.099f, 0, 0));
        Assert.Equal(0, Rac1AridiaConstructorbotNative.WalkerFlags(.1f, 0, 0));
        Assert.Equal(0x14, Rac1AridiaConstructorbotNative.WalkerFlags(.099f, MathF.PI, 0));
        Assert.Equal(0x16, Rac1AridiaConstructorbotNative.WalkerFlags(.099f, MathF.PI, 0x2));
        Assert.Equal(0x26, Rac1AridiaConstructorbotNative.WalkerFlags(.05f, 0, 0x22));
        Assert.Equal(0x5, Rac1AridiaConstructorbotNative.WalkerFlags(0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.WalkerFlags(-1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.WalkerFlags(0, float.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.WalkerFlags(0, 0, 0x40));
    }

    [Fact]
    public void OriginalWalkerFixedPointCollisionRadiusIsNotBehaviorFlags()
    {
        Assert.Equal(640, Rac1AridiaConstructorbotNative.WalkerCollisionRadiusFixed);
        Assert.Equal(.625f, Rac1AridiaConstructorbotNative.WalkerCollisionRadius);
        Assert.Equal(0, Rac1AridiaConstructorbotNative.WalkerInitialRecordFlags);
        Assert.Equal(0, Rac1AridiaConstructorbotNative.WalkerRecordBehaviorFlags);
        Assert.Equal(0x22, Rac1AridiaConstructorbotNative.WalkerInitialProbeFlags(
            false, Rac1AridiaConstructorbotNative.WalkerRecordBehaviorFlags));
        Assert.Equal(0, Rac1AridiaConstructorbotNative.WalkerInitialProbeFlags(
            true, Rac1AridiaConstructorbotNative.WalkerRecordBehaviorFlags));
    }

    [Fact]
    public void SourceSweepOnlyStartsWhenRequestedMotionStrictlyExceedsNativeRadius()
    {
        Vector3 old = new(1, 2, 3);
        Assert.Null(Rac1AridiaConstructorbotNative.BuildWalkerSweep(
            old, Vector3.Zero, .625f, 1));
        Assert.Null(Rac1AridiaConstructorbotNative.BuildWalkerSweep(
            old, new(.625f, 0, 0), .625f, 1));
        var sweep = Rac1AridiaConstructorbotNative.BuildWalkerSweep(
            old, new(1, 0, 0), .625f, 1);
        Assert.True(sweep.HasValue);
        Assert.Equal(new(1, 2, 4), sweep.Value.Start);
        Assert.Equal(new(2.625f, 2, 4), sweep.Value.End);
        Assert.Equal(new(.625f, 0, 0), sweep.Value.RadiusAdvance);
        // Source hit is backed off by the radius AND then un-raised.
        Assert.Equal(new(1.625f, 2, 3),
            Rac1AridiaConstructorbotNative.WalkerSweptHitPosition(
                new(2.25f, 2, 4), sweep.Value, 1));

        var diagonal = Rac1AridiaConstructorbotNative.BuildWalkerSweep(
            Vector3.Zero, new(3, 4, 0), .625f, 1)!.Value;
        Assert.Equal(new(0, 0, 1), diagonal.Start);
        Assert.Equal(new(.375f, .5f, 0), diagonal.RadiusAdvance);
        Assert.Equal(new(3.375f, 4.5f, 1), diagonal.End);
        // Source vec3_length includes the height component.
        Assert.NotNull(Rac1AridiaConstructorbotNative.BuildWalkerSweep(
            Vector3.Zero, new(0, 0, .7f), .625f, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.BuildWalkerSweep(
                Vector3.Zero, Vector3.UnitX, -.01f, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.BuildWalkerSweep(
                Vector3.Zero, new(float.NaN, 0, 0), .625f, 1));
    }

    [Fact]
    public void WalkerContactOnlyMarksActualTouchAndForwardTravelBelowNativeTwentyPercent()
    {
        Vector3 requested = new(1, 0, 0);
        var untouched = Rac1AridiaConstructorbotNative.WalkerPostMoveContact(
            requested, Vector3.Zero, 0, 10, null);
        Assert.Equal(new(0, false), untouched);
        // walker_move bit2 is floor contact, NOT a walker_record return bit.
        var floorOnly = Rac1AridiaConstructorbotNative.WalkerPostMoveContact(
            requested, new(.6f, 0, 0), 2, 10, null);
        Assert.Equal(new(0, false), floorOnly);
        var mild = Rac1AridiaConstructorbotNative.WalkerPostMoveContact(
            requested, new(.201f, .5f, 0), 3, 10, 9.9f);
        Assert.Equal(new(1, false), mild);
        var exact = Rac1AridiaConstructorbotNative.WalkerPostMoveContact(
            requested, new(.2f, .5f, 0), 1, 10, 10);
        Assert.Equal(new(1, false), exact);
        var blocked = Rac1AridiaConstructorbotNative.WalkerPostMoveContact(
            requested, new(.199f, .5f, 0), 1, 10, 10);
        Assert.Equal(new(3, false), blocked);
        var backwards = Rac1AridiaConstructorbotNative.WalkerPostMoveContact(
            requested, new(-.25f, 0, 0), 1, 10, 10);
        Assert.Equal(new(3, false), backwards);
        var orthogonal = Rac1AridiaConstructorbotNative.WalkerPostMoveContact(
            new(0, 4, 0), new(2, 0, 0), 1, 10, 10);
        Assert.Equal(new(3, false), orthogonal);
    }

    [Fact]
    public void WalkerContactRollbackOnlyAfterActualTouchAndInvalidFollowUpFloor()
    {
        Vector3 request = new(1, 0, 0);
        Assert.Equal(new(0x23, true), Rac1AridiaConstructorbotNative.WalkerPostMoveContact(
            request, new(.6f, 0, 0), 1, 10, null));
        Assert.Equal(new(0x23, true), Rac1AridiaConstructorbotNative.WalkerPostMoveContact(
            request, new(.6f, 0, 0), 1, 10, 9.499f));
        Assert.Equal(new(0x23, true), Rac1AridiaConstructorbotNative.WalkerPostMoveContact(
            request, new(.6f, 0, 0), 1, 10, 10.501f));
        Assert.Equal(new(1, false), Rac1AridiaConstructorbotNative.WalkerPostMoveContact(
            request, new(.6f, 0, 0), 1, 10, 9.5f));
        Assert.Equal(new(1, false), Rac1AridiaConstructorbotNative.WalkerPostMoveContact(
            request, new(.6f, 0, 0), 1, 10, 10.5f));
        Assert.Equal(new(0, false), Rac1AridiaConstructorbotNative.WalkerPostMoveContact(
            request, Vector3.Zero, 0, 10, null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.WalkerPostMoveContact(
                request, new(float.NaN, 0, 0), 1, 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.WalkerPostMoveContact(
                request, Vector3.Zero, 4, 10, 10));
    }

    [Fact]
    public void NativeWalkerMoveFloorLiftAppliesSourceThirtyPercentWithoutForcedDrop()
    {
        Assert.Equal(.075f, Rac1AridiaConstructorbotNative.WalkerGroundLift(
            10, 9.75f, .5f), 5);
        Assert.Equal(0, Rac1AridiaConstructorbotNative.WalkerGroundLift(
            10, 9.25f, .5f));
        Assert.Equal(0, Rac1AridiaConstructorbotNative.WalkerGroundLift(
            10, 9.5f, .5f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.WalkerGroundLift(10, 9.5f, -.1f));
        Assert.Equal(6, Rac1AridiaConstructorbotNative.WalkerSpherePushoutAttempts);
        Assert.Equal(.05f, Rac1AridiaConstructorbotNative.WalkerMoveGroundProbeExtension);
    }

    [Fact]
    public void WalkerSourceSpeedAcceleratesDragsAndClampsWithoutOverwritingPhysics()
    {
        float speed = 0;
        speed = Rac1AridiaConstructorbotNative.WalkerSpeed(speed, 100f, 1f);
        Assert.Equal(.3f, speed, 4);
        speed = Rac1AridiaConstructorbotNative.WalkerSpeed(speed, 100f, 1f);
        Assert.Equal(.6f, speed, 4);
        for (int i = 0; i < 30; i++)
            speed = Rac1AridiaConstructorbotNative.WalkerSpeed(speed, 100f, 1f);
        Assert.Equal(Rac1AridiaConstructorbotNative.WalkerMaxSpeed, speed, 4);
        float near = Rac1AridiaConstructorbotNative.WalkerSpeed(1f, .05f, 1f);
        Assert.InRange(near, .70f, .72f);
        Assert.Equal(0f, Rac1AridiaConstructorbotNative.WalkerSpeed(.7f, 10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.WalkerSpeed(float.NaN, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.WalkerSpeed(0, 0, 1.1f));
    }

    [Fact]
    public void WalkerStateFiveNeedsExactlyBitFourAndStateSixTestsBitsTwoOrFour()
    {
        // LEVEL2:0x2d7b6c tests the entire return word ==4, not a bitwise flag.
        Assert.Equal(new(7, 0), Rac1AridiaConstructorbotNative.StepWalkerState(5, 4, true));
        foreach (int flags in new[] { 0, 1, 2, 5, 6, 8, 0x14, 0x24 })
            Assert.Equal(new(5, 6), Rac1AridiaConstructorbotNative.StepWalkerState(5, flags, true));
        Assert.Equal(new(5, 6), Rac1AridiaConstructorbotNative.StepWalkerState(5, 4, false));

        Assert.Equal(new(7, 0), Rac1AridiaConstructorbotNative.StepWalkerState(6, 0, true));
        Assert.Equal(new(7, 0), Rac1AridiaConstructorbotNative.StepWalkerState(6, 0x10, true));
        Assert.Equal(new(5, 6), Rac1AridiaConstructorbotNative.StepWalkerState(6, 2, true));
        Assert.Equal(new(5, 6), Rac1AridiaConstructorbotNative.StepWalkerState(6, 4, true));
        Assert.Equal(new(6, 6), Rac1AridiaConstructorbotNative.StepWalkerState(6, 0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.StepWalkerState(7, 0, true));
    }

    [Fact]
    public void SourceApproachScalarClampsBothSignsAndReachesExactTarget()
    {
        Assert.Equal(1.25f, Rac1AridiaConstructorbotNative.ApproachNativeScalar(1, 2, .25f));
        Assert.Equal(.75f, Rac1AridiaConstructorbotNative.ApproachNativeScalar(1, 0, .25f));
        Assert.Equal(2, Rac1AridiaConstructorbotNative.ApproachNativeScalar(1.99f, 2, .25f));
        Assert.Equal(0, Rac1AridiaConstructorbotNative.ApproachNativeScalar(.01f, 0, .25f));
        Assert.Equal(1, Rac1AridiaConstructorbotNative.ApproachNativeScalar(1, 2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.ApproachNativeScalar(0, 1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.ApproachNativeScalar(float.NaN, 1, 1));
    }

    [Fact]
    public void StateThreeVerticalPreservesSpeedRampAndOnlyLandsOnActualFloor()
    {
        float height = 32;
        float speed = 0;
        for (int i = 0; i < 8; i++)
        {
            var frame = Rac1AridiaConstructorbotNative.StepStateThreeVertical(
                height, speed, 24.9f, 1f / 60f, 1f / 60f);
            Assert.InRange(frame.VerticalStep, 0, 10f / 60);
            Assert.True(frame.Height <= height);
            Assert.False(frame.ReachedFloor);
            speed = frame.VerticalStep;
            height = frame.Height;
        }
        Assert.Equal(8f / 60, speed, 5);
        bool landed = false;
        for (int i = 0; i < 200; i++)
        {
            var frame = Rac1AridiaConstructorbotNative.StepStateThreeVertical(
                height, speed, 24.9f, 1f / 60f, 1f / 60f);
            height = frame.Height;
            speed = frame.VerticalStep;
            if (!frame.ReachedFloor) continue;
            landed = true;
            break;
        }
        Assert.True(landed);
        Assert.Equal(24.9f, height);
        Assert.Equal(10f / 60, speed, 5);
        var noFloor = Rac1AridiaConstructorbotNative.StepStateThreeVertical(
            height, speed, null, 1f / 60f, 1f / 60f);
        Assert.Equal(height, noFloor.Height);
        Assert.False(noFloor.ReachedFloor);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.StepStateThreeVertical(10, 0, float.NaN, 1f / 60f, .01f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.StepStateThreeVertical(10, 0, 0, 1f / 60f, -.01f));
    }

    [Fact]
    public void StateThreeHorizontalClampsTowardSourceWaypointWithoutMovingVerticalCoordinate()
    {
        var current = new Vector3(0, 0, 100);
        var waypoint = new Vector3(3, 4, -200);
        var step = Rac1AridiaConstructorbotNative.StepStateThreeHorizontal(
            current, waypoint, 1f / 60f);
        Assert.InRange(step.Length(), .0999f, .1001f);
        Assert.Equal(0, step.Z);
        Assert.InRange(step.X, .0599f, .0601f);
        Assert.InRange(step.Y, .0799f, .0801f);

        Vector3 location = current;
        for (int i = 0; i < 50; i++)
            location += Rac1AridiaConstructorbotNative.StepStateThreeHorizontal(
                location, waypoint, 1f / 60f);
        Assert.InRange(location.X, 2.999f, 3.001f);
        Assert.InRange(location.Y, 3.999f, 4.001f);
        Assert.Equal(100, location.Z);

        Assert.Equal(Vector3.Zero, Rac1AridiaConstructorbotNative.StepStateThreeHorizontal(
            location, waypoint, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.StepStateThreeHorizontal(current, waypoint, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.StepStateThreeHorizontal(
                current, new(float.NaN, 0, 0), 1f / 60f));
    }

    [Fact]
    public void StateThreeYawCapsPerFrameRotationTowardTrackedTarget()
    {
        float max = MathF.PI / 120f;
        Assert.InRange(Rac1AridiaConstructorbotNative.StepStateThreeYaw(
            0, MathF.PI, 1f / 60f), -max - .00001f, max + .00001f);
        Assert.Equal(.01f, Rac1AridiaConstructorbotNative.StepStateThreeYaw(0, .01f, 1f / 60f), 4);
        Assert.InRange(Rac1AridiaConstructorbotNative.StepStateThreeYaw(
            0, -.5f, 1f / 60f), -max - .00001f, -max + .00001f);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.StepStateThreeYaw(0, float.NaN, 1f / 60f));
    }

    [Fact]
    public void StateThreeLandingAndFourCompletionRequireActualSourceSignals()
    {
        for (int i = 0; i < 600; i++)
            Assert.Equal(new(3, 4), Rac1AridiaConstructorbotNative.StepStateThreeLanding(null));
        Assert.Equal(new(3, 4), Rac1AridiaConstructorbotNative.StepStateThreeLanding(false));
        Assert.Equal(new(4, 5), Rac1AridiaConstructorbotNative.StepStateThreeLanding(true));
        Assert.Equal(new(4, 5), Rac1AridiaConstructorbotNative.StepStateFour(false));
        Assert.Equal(new(5, 6), Rac1AridiaConstructorbotNative.StepStateFour(true));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(16)]
    public void SourceStateTwoRetainsActivationClipUntilAnimationCompletion(int sequence)
    {
        for (int tick = 0; tick < 200; tick++)
            Assert.Equal(new(2, sequence),
                Rac1AridiaConstructorbotNative.StepActivatedAnimation(false, sequence));
        Assert.Equal(new(3, 4),
            Rac1AridiaConstructorbotNative.StepActivatedAnimation(true, sequence));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.StepActivatedAnimation(true, 15));
    }

    [Fact]
    public void NativeStateTwelveChecksAnimationThenRangeAndHeightWithoutInventedCooldown()
    {
        Assert.Equal(new(12, 9), Rac1AridiaConstructorbotNative.StepFollowup(false, 500, 25, 18));
        Assert.Equal(new(12, 9), Rac1AridiaConstructorbotNative.StepFollowup(true, 18.99f, 2.99f, 18));
        Assert.Equal(new(7, 0), Rac1AridiaConstructorbotNative.StepFollowup(true, 19f, 0, 18));
        Assert.Equal(new(7, 0), Rac1AridiaConstructorbotNative.StepFollowup(true, 0, 3f, 18));
        Assert.Equal(new(7, 0), Rac1AridiaConstructorbotNative.StepFollowup(true, 0, -3f, 18));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.StepFollowup(true, float.NaN, 0, 18));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Rac1AridiaConstructorbotNative.StepFollowup(true, 0, float.PositiveInfinity, 18));
    }

    [Fact]
    public void NativeStateThirteenWaitsForPhysicsContactInsteadOfElapsedTicks()
    {
        for (int tick = 0; tick < 800; tick++)
            Assert.Equal(new(13, 10), Rac1AridiaConstructorbotNative.StepRecovery(false, 10));
        Assert.Equal(new(7, 0), Rac1AridiaConstructorbotNative.StepRecovery(true, 10));
    }

    [Fact]
    public void NativeActivationPvarSelectorsRejectUnknownAndMalformedValues()
    {
        var pvar = Pvar(3);
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x18c), -1);
        BinaryPrimitives.WriteSingleLittleEndian(pvar.AsSpan(0x184), 20);
        Assert.Equal(-1, Rac1AridiaConstructorbotNative.ReadActivationVolume(pvar));
        Assert.Equal(20, Rac1AridiaConstructorbotNative.ReadActivationDistance(pvar));
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x18c), 21);
        Assert.Equal(21, Rac1AridiaConstructorbotNative.ReadActivationVolume(pvar));
        BinaryPrimitives.WriteInt32LittleEndian(pvar.AsSpan(0x18c), -2);
        Assert.Throws<InvalidDataException>(() =>
            Rac1AridiaConstructorbotNative.ReadActivationVolume(pvar));
        BinaryPrimitives.WriteSingleLittleEndian(pvar.AsSpan(0x184), float.NaN);
        Assert.Throws<InvalidDataException>(() =>
            Rac1AridiaConstructorbotNative.ReadActivationDistance(pvar));
        Assert.Throws<InvalidDataException>(() =>
            Rac1AridiaConstructorbotNative.ReadActivationVolume(new byte[0x18c]));
    }

    [Fact]
    public void NativeAimAndJointContactAreDifferentStates()
    {
        Assert.Equal(10, Rac1AridiaConstructorbotNative.AimState);
        Assert.Equal(11, Rac1AridiaConstructorbotNative.JointContactState);
        Assert.Equal(12, Rac1AridiaConstructorbotNative.FollowupState);
        Assert.Equal(13, Rac1AridiaConstructorbotNative.ContactRecoveryState);
        Assert.Equal(new(10, 0), Rac1AridiaConstructorbotNative.StepAim(false));
        Assert.Equal(new(12, 9), Rac1AridiaConstructorbotNative.StepAim(true));

        var contact = Rac1AridiaConstructorbotNative.StepJointContact(false, false);
        Assert.Equal(new(11, 8, true), contact);
        Assert.True(contact.JointContactQuery);
        Assert.Equal(new(13, 8, true), Rac1AridiaConstructorbotNative.StepJointContact(true, false));
        Assert.Equal(new(13, 10, true), Rac1AridiaConstructorbotNative.StepJointContact(true, true));
    }
}
