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
    public void RespawnCannotInventAResetWhileAlive()
    {
        var session = new Rac1RatchetNanotechSession();

        Assert.Throws<InvalidOperationException>(() => session.Respawn());
        Assert.Equal(4, session.Probe().Nanotech);
    }

    private static Rac1Class749AttackEvent RecoveredClass749Attack() =>
        new(Rac1Class749Hostile.AttackMarker, Rac1Class749Hostile.AttackDamage);
}
