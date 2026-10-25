using OBP.Core;
using System.Numerics;
using OBP.RAC2.Gameplay;

namespace OBP.Tests;

public sealed class Rac2LancerSessionTests
{
    [Fact]
    public void HeldTriggerRespectsEquipStateAndNineTickRepeatPeriod()
    {
        var session = new Rac2LancerSession(200);
        Assert.Equal(Rac2LancerFireStatus.NotEquipped, Tick(session).Status);
        session.Equip();
        var firedAt = new List<int>();
        for (int tick = 1; tick <= 60; tick++)
        {
            var result = Tick(session);
            if (tick <= 7) Assert.Equal(Rac2LancerFireStatus.Equipping, result.Status);
            if (result.Shot is { } shot)
            {
                firedAt.Add(tick);
                Assert.Equal(ObpSourceGame.Rac2, result.SourceGame);
                Assert.Equal(30, result.WeaponIndex);
                Assert.Equal(ObpSourceGame.Rac2, shot.SourceGame);
                Assert.Equal(3070, shot.NativeClassId);
            }
        }

        Assert.Equal(new[] { 8, 17, 26, 35, 44, 53 }, firedAt);
        Assert.Equal(194, session.Ammo);
        Assert.Equal(6, session.ActiveShots.Count);
    }

    [Fact]
    public void EmptyFireRejectsAndThrottlesWithoutCreatingAShot()
    {
        var session = new Rac2LancerSession(1);
        _ = FirstShot(session);
        Assert.Equal(0, session.Ammo);
        for (int tick = 1; tick < 9; tick++)
            Assert.Equal(Rac2LancerFireStatus.CoolingDown, Tick(session).Status);

        var rejected = Tick(session);
        Assert.Equal(Rac2LancerFireStatus.NoAmmo, rejected.Status);
        Assert.Null(rejected.Shot);
        Assert.Equal(0, session.Ammo);
        Assert.Single(session.ActiveShots);
        Assert.Equal(Rac2LancerFireStatus.CoolingDown, Tick(session).Status);
    }

    [Fact]
    public void ReleasedTriggerAdvancesCooldownWithoutQueuingOrSpendingAmmo()
    {
        var session = new Rac2LancerSession(2);
        _ = FirstShot(session);
        for (int tick = 0; tick < 20; tick++)
            Assert.Equal(Rac2LancerFireStatus.TriggerReleased, Tick(session, false).Status);

        Assert.Equal(1, session.Ammo);
        Assert.Equal(Rac2LancerFireStatus.Fired, Tick(session).Status);
        Assert.Equal(0, session.Ammo);
    }

    [Fact]
    public void EquipIsIdempotentAndReequipPreservesAmmoAndExistingFlight()
    {
        var session = new Rac2LancerSession(5);
        var shot = FirstShot(session);
        session.Equip();
        Assert.Equal(Rac2LancerFireStatus.CoolingDown, Tick(session).Status);
        session.Unequip();
        Assert.Equal(4, session.Ammo);
        Assert.NotNull(session.BeginProjectileTick(shot));
        session.Equip();
        Assert.Equal(Rac2LancerFireStatus.Equipping, Tick(session).Status);
        Assert.Equal(4, session.Ammo);
    }

    [Fact]
    public void AmmoAdmissionCapsWithoutIntegerOverflow()
    {
        var session = new Rac2LancerSession(2);
        Assert.Equal(198, session.AddAmmo(int.MaxValue));
        Assert.Equal(200, session.Ammo);
        Assert.Equal(0, session.AddAmmo(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.AddAmmo(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rac2LancerSession(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rac2LancerSession(201));
    }

    [Fact]
    public void FlightSweepsOneNativeUnitPerTickAndExpiresAfterTestingFinalSegment()
    {
        var session = new Rac2LancerSession(1);
        var shot = FirstShot(session);
        Assert.Equal(new Vector3(1, 2, 3), shot.Origin);
        Assert.Equal(Vector3.UnitX, shot.Direction);
        Assert.Equal(new Vector3(60, 0, 0), shot.Velocity);

        for (int tick = 1; tick <= 20; tick++)
        {
            var sweep = session.BeginProjectileTick(shot)!;
            Assert.Equal(tick, sweep.Tick);
            Assert.Equal(new Vector3(tick, 2, 3), sweep.From);
            Assert.Equal(new Vector3(tick + 1, 2, 3), sweep.To);
            Assert.True(session.TryCompleteProjectileTick(sweep, null, out var impact));
            Assert.Null(impact);
        }

        Assert.Empty(session.ActiveShots);
        Assert.Null(session.BeginProjectileTick(shot));
    }

    [Fact]
    public void ForeignWorldTargetGetsRac2DamageExactlyOnce()
    {
        var session = new Rac2LancerSession(1);
        var shot = FirstShot(session);
        var sweep = session.BeginProjectileTick(shot)!;
        var target = new Rac2LancerTarget(ObpSourceGame.Rac1, 500, 123);
        Assert.True(session.TryCompleteProjectileTick(sweep, new(0.25f, target), out var impact));
        Assert.NotNull(impact);
        Assert.Equal(new Vector3(1.25f, 2, 3), impact.Position);
        Assert.Equal(ObpSourceGame.Rac2, impact.SourceGame);
        Assert.NotNull(impact.Damage);
        Assert.Equal(target, impact.Damage.Target);
        Assert.Equal(ObpSourceGame.Rac2, impact.Damage.SourceGame);
        Assert.Equal(30, impact.Damage.WeaponIndex);
        Assert.Equal(new Rac2CollisionDamage(0x10000, 1, 1, 3070, 0.5f, 1), impact.Damage.Damage);
        Assert.False(session.TryCompleteProjectileTick(sweep, new(0.25f, target), out var duplicate));
        Assert.Null(duplicate);
        Assert.Null(session.BeginProjectileTick(shot));
    }

    [Fact]
    public void WorldBlockStopsFlightWithoutAnEntityDamageIntent()
    {
        var session = new Rac2LancerSession(1);
        var sweep = session.BeginProjectileTick(FirstShot(session))!;
        Assert.True(session.TryCompleteProjectileTick(sweep, new(0.5f), out var impact));
        Assert.NotNull(impact);
        Assert.Null(impact.Damage);
        Assert.Empty(session.ActiveShots);
    }

    [Fact]
    public void ContactOnLastTravelTickIsNotLostToExpiry()
    {
        var session = new Rac2LancerSession(1);
        var shot = FirstShot(session);
        for (int i = 0; i < 19; i++)
            Assert.True(session.TryCompleteProjectileTick(session.BeginProjectileTick(shot)!, null, out _));

        var final = session.BeginProjectileTick(shot)!;
        Assert.True(session.TryCompleteProjectileTick(final, new(1, new(ObpSourceGame.Rac3, 99, 1)), out var impact));
        Assert.NotNull(impact!.Damage);
        Assert.Equal(new Vector3(21, 2, 3), impact.Position);
    }

    [Fact]
    public void ResetRejectsOldAndForeignCapabilitiesEvenWhenNumericIdsCollide()
    {
        var session = new Rac2LancerSession(2);
        var oldShot = FirstShot(session);
        var oldSweep = session.BeginProjectileTick(oldShot)!;
        var foreign = new Rac2LancerSession(1);
        var foreignShot = FirstShot(foreign);
        var foreignSweep = foreign.BeginProjectileTick(foreignShot)!;
        session.ResetWorld(10);
        Assert.False(session.Equipped);
        Assert.Equal(0, session.CooldownSeconds);
        Assert.Empty(session.ActiveShots);
        var current = FirstShot(session);
        Assert.Equal(oldShot.Sequence, current.Sequence);
        Assert.Equal(foreignShot.Sequence, current.Sequence);
        Assert.True(current.Generation > oldShot.Generation);
        Assert.Null(session.BeginProjectileTick(oldShot));
        Assert.Null(session.BeginProjectileTick(foreignShot));
        Assert.False(session.TryCompleteProjectileTick(oldSweep, new(0), out _));
        Assert.False(session.TryCompleteProjectileTick(foreignSweep, new(0), out _));
        Assert.Equal(9, session.Ammo);
        Assert.Single(session.ActiveShots);
    }

    [Fact]
    public void InvalidInputAndPendingSweepMisuseLeaveStateAvailableForRetry()
    {
        var session = new Rac2LancerSession(2);
        var shot = FirstShot(session);
        float cooldown = session.CooldownSeconds;
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Tick(true, Vector3.Zero, Vector3.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Tick(true, new(float.NaN, 0, 0), Vector3.UnitX));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.ResetWorld(201));
        Assert.Equal(cooldown, session.CooldownSeconds);
        Assert.Equal(1, session.Ammo);
        var sweep = session.BeginProjectileTick(shot)!;
        Assert.Throws<InvalidOperationException>(() => session.BeginProjectileTick(shot));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.TryCompleteProjectileTick(sweep, new(float.NaN), out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.TryCompleteProjectileTick(sweep, new(1.01f), out _));
        Assert.Throws<ArgumentException>(() => session.TryCompleteProjectileTick(sweep, new(0, new((ObpSourceGame)(-1), 0, 0)), out _));
        Assert.True(session.TryCompleteProjectileTick(sweep, null, out _));
        Assert.False(session.TryCompleteProjectileTick(sweep, new(0), out _));
        Assert.Equal(2, session.BeginProjectileTick(shot)!.Tick);
    }

    [Fact]
    public void IdenticalInputsProduceIdenticalObservableShotsAndContacts()
    {
        static string[] Run()
        {
            var session = new Rac2LancerSession(3);
            session.Equip();
            var trace = new List<string>();
            for (int tick = 0; tick < 40; tick++)
            {
                var result = Tick(session, tick < 30);
                trace.Add($"{result.Status}:{session.Ammo}:{session.CooldownSeconds:R}");
                foreach (var shot in session.ActiveShots)
                {
                    var sweep = session.BeginProjectileTick(shot)!;
                    var contact = sweep.Tick == 4 ? new Rac2LancerContact(0.5f, new(ObpSourceGame.Rac1, 500, 1)) : null;
                    Assert.True(session.TryCompleteProjectileTick(sweep, contact, out var impact));
                    trace.Add($"{shot.Generation}:{shot.Sequence}:{sweep.Tick}:{sweep.To}:{impact?.Damage?.Damage}");
                }
            }

            return trace.ToArray();
        }

        Assert.Equal(Run(), Run());
    }

    private static Rac2LancerFireResult Tick(Rac2LancerSession session, bool held = true) =>
        session.Tick(held, new(1, 2, 3), new(2, 0, 0));

    private static Rac2LancerShot FirstShot(Rac2LancerSession session)
    {
        session.Equip();
        for (int tick = 0; tick < 7; tick++)
            Assert.Equal(Rac2LancerFireStatus.Equipping, Tick(session).Status);
        var result = Tick(session);
        Assert.Equal(Rac2LancerFireStatus.Fired, result.Status);
        return Assert.IsType<Rac2LancerShot>(result.Shot);
    }
}
