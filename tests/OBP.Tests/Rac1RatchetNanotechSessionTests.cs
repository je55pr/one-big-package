using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1RatchetNanotechSessionTests
{
    [Fact]
    public void StartsWithRetailVeldinNanotech()
    {
        var session = new Rac1RatchetNanotechSession();

        var probe = session.Probe();

        Assert.Equal(4, probe.Nanotech);
        Assert.Equal(4, probe.RespawnNanotech);
        Assert.Equal(Rac1RatchetLifeState.Alive, probe.LifeState);
        Assert.False(probe.IsDead);
    }

    [Fact]
    public void RecoveredClass749AttackConsumesOneNanotech()
    {
        var session = new Rac1RatchetNanotechSession();

        var probe = session.ApplyClass749Attack(RecoveredClass749Attack());

        Assert.Equal(3, probe.Nanotech);
        Assert.Equal(Rac1RatchetLifeState.Alive, probe.LifeState);
    }

    [Fact]
    public void FourRecoveredClass749HitsReachZeroAndDeath()
    {
        var session = new Rac1RatchetNanotechSession();
        Rac1RatchetNanotechSnapshot probe = session.Probe();

        for (int hit = 0; hit < 4; hit++)
            probe = session.ApplyClass749Attack(RecoveredClass749Attack());

        Assert.Equal(0, probe.Nanotech);
        Assert.Equal(Rac1RatchetLifeState.Dead, probe.LifeState);
        Assert.True(probe.IsDead);
        Assert.Throws<InvalidOperationException>(() =>
            session.ApplyClass749Attack(RecoveredClass749Attack()));
    }

    [Theory]
    [InlineData(33.0, 1.0)]
    [InlineData(34.0, 2.0)]
    public void RejectsUnrecoveredEnemyDamageEvents(double marker, double damage)
    {
        var session = new Rac1RatchetNanotechSession();

        Assert.Throws<NotSupportedException>(() =>
            session.ApplyClass749Attack(new Rac1Class749AttackEvent(marker, damage)));
        Assert.Equal(4, session.Probe().Nanotech);
    }

    [Fact]
    public void VeldinEnvironmentalDeathBoundaryAndRespawnRestoreRetailNanotech()
    {
        var session = new Rac1RatchetNanotechSession();

        var dead = session.ApplyEnvironmentalDeathReset();
        Assert.Equal(0, dead.Nanotech);
        Assert.True(dead.IsDead);

        var respawned = session.Respawn();
        Assert.Equal(4, respawned.Nanotech);
        Assert.Equal(Rac1RatchetLifeState.Alive, respawned.LifeState);
    }

    [Fact]
    public void VeldinDeathPlaneUsesStrictVerticalBoundaryAndRestartsRecoveredDeathSequence()
    {
        var session = new Rac1RatchetNanotechSession();

        Assert.Null(session.TryApplyVeldinEnvironmentalDeath(DeathFacts(nativeVerticalPosition: 27d)));
        Assert.Equal(Rac1RatchetLifeState.Alive, session.Probe().LifeState);

        var dead = Assert.IsType<Rac1RatchetNanotechSnapshot>(session.TryApplyVeldinEnvironmentalDeath(
            DeathFacts(nativeVerticalPosition: Math.BitDecrement(27d))));
        Assert.Equal(0, dead.Nanotech);
        Assert.Equal(Rac1RatchetNanotechSession.RetailVeldinDeathNativeState, dead.NativePlayerState);
        Assert.Equal(Rac1RatchetNanotechSession.RetailVeldinDeathNativeSequence, dead.NativeSequence);
        Assert.Equal(Rac1RatchetNanotechSession.RetailVeldinDeathNativeSequenceFrame, dead.NativeSequenceFrame);
    }

    [Fact]
    public void VeldinDeathPlaneUsesStrictContactSeparationBoundary()
    {
        var session = new Rac1RatchetNanotechSession();

        Assert.Null(session.TryApplyVeldinEnvironmentalDeath(DeathFacts(contactSeparation: 2d)));
        var dead = session.TryApplyVeldinEnvironmentalDeath(
            DeathFacts(contactSeparation: Math.BitIncrement(2d)));

        Assert.NotNull(dead);
        Assert.True(dead!.IsDead);
    }

    [Fact]
    public void VeldinDeathPlaneExcludesOnlyRecoveredRaw20A4ValueTwo()
    {
        var session = new Rac1RatchetNanotechSession();

        Assert.Null(session.TryApplyVeldinEnvironmentalDeath(DeathFacts(nativePlayerState20A4: 2)));
        Assert.NotNull(session.TryApplyVeldinEnvironmentalDeath(DeathFacts(nativePlayerState20A4: 0)));
    }

    [Fact]
    public void VeldinEnvironmentalDeathResetIsOneShotWhileAlive()
    {
        var session = new Rac1RatchetNanotechSession();
        var facts = DeathFacts();

        var first = Assert.IsType<Rac1RatchetNanotechSnapshot>(session.TryApplyVeldinEnvironmentalDeath(facts));
        Assert.Null(session.TryApplyVeldinEnvironmentalDeath(facts));

        Assert.Equal(first, session.Probe());
        Assert.Throws<InvalidOperationException>(() => session.ApplyEnvironmentalDeathReset());
    }

    [Fact]
    public void VeldinRespawnClearsRecoveredDeathTransitionAndRestoresNanotech()
    {
        var session = new Rac1RatchetNanotechSession();
        Assert.NotNull(session.TryApplyVeldinEnvironmentalDeath(DeathFacts()));

        var respawn = session.Respawn();

        Assert.Equal(4, respawn.Nanotech);
        Assert.Equal(Rac1RatchetLifeState.Alive, respawn.LifeState);
        Assert.Null(respawn.NativePlayerState);
        Assert.Null(respawn.NativeSequence);
        Assert.Null(respawn.NativeSequenceFrame);
    }

    [Fact]
    public void RespawnCannotInventAResetWhileAlive()
    {
        var session = new Rac1RatchetNanotechSession();

        Assert.Throws<InvalidOperationException>(() => session.Respawn());
        Assert.Equal(4, session.Probe().Nanotech);
    }

    private static Rac1VeldinEnvironmentalDeathFacts DeathFacts(
        double nativeVerticalPosition = 26d,
        double contactSeparation = 3d,
        int nativePlayerState20A4 = 0) =>
        new(nativeVerticalPosition, 27d, contactSeparation, nativePlayerState20A4);

    private static Rac1Class749AttackEvent RecoveredClass749Attack() =>
        new(Rac1Class749Hostile.AttackMarker, Rac1Class749Hostile.AttackDamage);
}
