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
