using OBP.RAC2.Gameplay;

namespace OBP.Tests;

public sealed class GcClass2827OpeningEncounterTests
{
    [Fact]
    public void ApproachRampsByFiveThousandthsPerTickAndCapsAtOneTenth()
    {
        var approach = new GcClass2827ApproachSession();

        var first = approach.Advance(0, 0, 20, 0);
        Assert.Equal(0.005d, first.PlanarStep, 9);
        Assert.Equal(0.005d, first.X, 9);

        var twentieth = approach.Advance(first.X, first.Z, 20, 0, ticks: 19);
        Assert.Equal(0.1d, twentieth.PlanarStep, 9);

        var later = approach.Advance(twentieth.X, twentieth.Z, 20, 0, ticks: 20);
        Assert.Equal(0.1d, later.PlanarStep, 9);
    }

    [Fact]
    public void ApproachStopsAtRecoveredAttackEntryDistance()
    {
        var approach = new GcClass2827ApproachSession();
        double target = 20d;

        var result = approach.Advance(0, 0, target, 0, ticks: 500);
        double remaining = target - result.X;

        Assert.Equal(
            GcClass2827HostileSession.AttackEntryDistanceExclusive,
            remaining,
            precision: 5);
    }

    [Fact]
    public void FourRetailJointContactsCollapseToOnePlayerDamageRecordPerAttackCycle()
    {
        var cycle = new GcClass2827AttackCycleSession();
        var contacts = new[]
        {
            new GcClass2827AttackContact(0, 0.35f, 1f, 1f),
            new GcClass2827AttackContact(1, 0.15f, 1f, 1f),
            new GcClass2827AttackContact(2, 0.15f, 1f, 1f),
            new GcClass2827AttackContact(9, 0.35f, 1f, 1f),
        };

        cycle.Begin(GcClass2827HostileSession.AttackSequence27);
        var admitted = cycle.TryAggregatePlayerContact(contacts);
        var duplicateFrame = cycle.TryAggregatePlayerContact(contacts);

        Assert.NotNull(admitted);
        Assert.Equal(GcRatchetNanotechSession.DamageQueryMask, admitted!.Value.QueryMask);
        Assert.Equal(1f, admitted.Value.DamageHp);
        Assert.Null(duplicateFrame);

        cycle.End();
        cycle.Begin(GcClass2827HostileSession.AttackSequence27);
        Assert.NotNull(cycle.TryAggregatePlayerContact(contacts));
    }

    [Fact]
    public void EmptyOrInactiveAttackCycleCannotDamageRatchet()
    {
        var cycle = new GcClass2827AttackCycleSession();
        var one = new[] { new GcClass2827AttackContact(0, 0.35f, 1f, 1f) };

        Assert.Null(cycle.TryAggregatePlayerContact(one));

        cycle.Begin(GcClass2827HostileSession.AttackSequence16);
        Assert.Null(cycle.TryAggregatePlayerContact(Array.Empty<GcClass2827AttackContact>()));
    }

    [Theory]
    [InlineData(GcClass2827HostileSession.AttackSequence16, 100)]
    [InlineData(GcClass2827HostileSession.AttackSequence27, 114)]
    public void RetailWitnessPinsAttackCycleDurations(int sequence, int expectedTicks)
    {
        var cycle = new GcClass2827AttackCycleSession();
        cycle.Begin(sequence);

        Assert.Equal(expectedTicks, cycle.ObservedDurationTicks());
        Assert.Equal(45, GcClass2827AttackCycleSession.ObservedPlayerContactStateTick);
        Assert.Equal(90, GcClass2827AttackCycleSession.ObservedRecoveryDurationTicks);
    }
}
