using OBP.RAC2.Gameplay;

namespace OBP.Tests;

public sealed class Rac2Class2827OpeningEncounterTests
{
    [Fact]
    public void ApproachRampsByFiveThousandthsPerTickAndCapsAtOneTenth()
    {
        var approach = new Rac2Class2827ApproachSession();

        var first = approach.Advance(0, 0, 20, 0);
        Assert.Equal(0.005d, first.PlanarStep, 9);
        Assert.Equal(0.005d, first.X, 9);

        var twentieth = approach.Advance(first.X, first.Z, 20, 0, ticks: 19);
        Assert.Equal(0.1d, twentieth.PlanarStep, 9);

        var later = approach.Advance(twentieth.X, twentieth.Z, 20, 0, ticks: 20);
        Assert.Equal(0.1d, later.PlanarStep, 9);
    }

    [Fact]
    public void ApproachCrossesTheStrictRecoveredAttackEntryDistance()
    {
        var approach = new Rac2Class2827ApproachSession();
        double target = 20d;

        var result = approach.Advance(0, 0, target, 0, ticks: 500);
        double remaining = target - result.X;

        Assert.True(remaining < Rac2Class2827HostileSession.AttackEntryDistanceExclusive);
        Assert.InRange(remaining, Rac2Class2827HostileSession.AttackEntryDistanceExclusive - Rac2Class2827ApproachSession.PlanarMaxStepPerTick,
            Rac2Class2827HostileSession.AttackEntryDistanceExclusive);
        Assert.True(Rac2Class2827HostileSession.ShouldEnterAttack((float)remaining, 0));
    }

    [Fact]
    public void FourRetailJointContactsCollapseToOnePlayerDamageRecordPerAttackCycle()
    {
        var cycle = new Rac2Class2827AttackCycleSession();
        var contacts = new[]
        {
            new Rac2Class2827AttackContact(0, 0.35f, 1f, 1f),
            new Rac2Class2827AttackContact(1, 0.15f, 1f, 1f),
            new Rac2Class2827AttackContact(2, 0.15f, 1f, 1f),
            new Rac2Class2827AttackContact(9, 0.35f, 1f, 1f),
        };

        cycle.Begin(Rac2Class2827HostileSession.AttackSequence27);
        var admitted = cycle.TryAggregatePlayerContact(contacts);
        var duplicateFrame = cycle.TryAggregatePlayerContact(contacts);

        Assert.NotNull(admitted);
        Assert.Equal(Rac2RatchetNanotechSession.DamageQueryMask, admitted!.Value.QueryMask);
        Assert.Equal(1f, admitted.Value.DamageHp);
        Assert.Null(duplicateFrame);

        cycle.End();
        cycle.Begin(Rac2Class2827HostileSession.AttackSequence27);
        Assert.NotNull(cycle.TryAggregatePlayerContact(contacts));
    }

    [Fact]
    public void EmptyOrInactiveAttackCycleCannotDamageRatchet()
    {
        var cycle = new Rac2Class2827AttackCycleSession();
        var one = new[] { new Rac2Class2827AttackContact(0, 0.35f, 1f, 1f) };

        Assert.Null(cycle.TryAggregatePlayerContact(one));

        cycle.Begin(Rac2Class2827HostileSession.AttackSequence16);
        Assert.Null(cycle.TryAggregatePlayerContact(Array.Empty<Rac2Class2827AttackContact>()));
    }

    [Theory]
    [InlineData(Rac2Class2827HostileSession.AttackSequence16, 100)]
    [InlineData(Rac2Class2827HostileSession.AttackSequence27, 114)]
    public void RetailWitnessPinsAttackCycleDurations(int sequence, int expectedTicks)
    {
        var cycle = new Rac2Class2827AttackCycleSession();
        cycle.Begin(sequence);

        Assert.Equal(expectedTicks, cycle.ObservedDurationTicks());
        Assert.Equal(45, Rac2Class2827AttackCycleSession.ObservedPlayerContactStateTick);
        Assert.Equal(90, Rac2Class2827AttackCycleSession.ObservedRecoveryDurationTicks);
    }
}
