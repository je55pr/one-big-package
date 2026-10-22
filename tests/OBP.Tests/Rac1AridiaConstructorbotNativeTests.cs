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
