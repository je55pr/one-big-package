using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

public sealed class HudStateAdapterTests
{
    [Fact]
    public void Rac1ProjectionTracksAuthoritativeHealthWeaponAndAmmo()
    {
        var nanotech = new Rac1RatchetNanotechSession();
        var weapons = new Rac1WeaponInventory(
            ownsFirstRanged: true,
            firstRangedAmmo: 1);

        var initial = Rac1HudProjection.Capture(nanotech, weapons);

        Assert.Equal(4, initial.Health!.Current);
        Assert.Null(initial.Health.Capacity);
        Assert.Equal(HudLifeState.Alive, initial.Health.LifeState);
        Assert.Equal(Rac1HudProjection.WrenchPresentationKey, initial.CurrentWeapon!.PresentationKey);
        Assert.Null(initial.CurrentWeapon.Ammo);
        Assert.Null(initial.Bolts);
        Assert.True(weapons.TryEquip(Rac1WeaponId.FirstRanged));
        Assert.True(weapons.TryUseEquipped());
        var ranged = Rac1HudProjection.Capture(nanotech, weapons);

        Assert.Equal(Rac1HudProjection.BombGlovePresentationKey, ranged.CurrentWeapon!.PresentationKey);
        Assert.Equal(0, ranged.CurrentWeapon.Ammo!.Current);
        Assert.Equal(Rac1BombGlove.MaxAmmo, ranged.CurrentWeapon.Ammo.Capacity);

        nanotech.ApplyClass749Attack(new Rac1Class749AttackEvent(
            Rac1Class749Hostile.AttackMarker,
            Rac1Class749Hostile.AttackDamage));
        var damaged = Rac1HudProjection.Capture(nanotech, weapons);

        Assert.Equal(3, damaged.Health!.Current);
        Assert.Equal(HudLifeState.Alive, damaged.Health.LifeState);
    }

    [Fact]
    public void PublisherAdvancesRevisionAndNumbersFeedbackWithinEpoch()
    {
        var nanotech = new Rac1RatchetNanotechSession();
        var weapons = new Rac1WeaponInventory(true, firstRangedAmmo: 1);
        var publisher = new HudStateAdapter();
        var initial = publisher.BeginSession(Rac1HudProjection.Capture(nanotech, weapons));
        var before = nanotech.Probe();
        var after = nanotech.ApplyClass749Attack(new Rac1Class749AttackEvent(
            Rac1Class749Hostile.AttackMarker,
            Rac1Class749Hostile.AttackDamage));

        var first = publisher.Publish(
            Rac1HudProjection.Capture(nanotech, weapons),
            Rac1HudProjection.DamageFeedback(before, after));
        var second = publisher.Publish(
            Rac1HudProjection.Capture(nanotech, weapons),
            Rac1HudProjection.CollectedBoltFeedback(5));

        Assert.Equal(initial.Epoch, first.Epoch);
        Assert.Equal(initial.Epoch, second.Epoch);
        Assert.Equal(0, initial.Revision);
        Assert.Equal(1, first.Revision);
        Assert.Equal(2, second.Revision);
        Assert.Equal(1, first.Feedback.Single().EventId);
        Assert.Equal(2, second.Feedback.Single().EventId);
        Assert.Equal(-1, first.Feedback.Single().Delta);
        Assert.Equal(5, second.Feedback.Single().Delta);
    }
    [Fact]
    public void ResetAndReplacementDropStaleStateAndFeedback()
    {
        var publisher = new HudStateAdapter();
        var nanotech = new Rac1RatchetNanotechSession();
        var weapons = new Rac1WeaponInventory(true, firstRangedAmmo: 6);
        var rac1 = publisher.BeginSession(Rac1HudProjection.Capture(nanotech, weapons));

        var withPickup = publisher.Publish(
            Rac1HudProjection.Capture(nanotech, weapons),
            Rac1HudProjection.CollectedBoltFeedback(1));
        var empty = publisher.ResetSession();

        Assert.True(empty.Epoch > rac1.Epoch);
        Assert.Equal(0, empty.Revision);
        Assert.Null(empty.Health);
        Assert.Null(empty.Bolts);
        Assert.Null(empty.CurrentWeapon);
        Assert.Empty(empty.Feedback);
        Assert.Single(withPickup.Feedback);

        var replacement = publisher.BeginSession(Rac1HudProjection.Capture(
            new Rac1RatchetNanotechSession(),
            new Rac1WeaponInventory(true, firstRangedAmmo: 3)));

        Assert.True(replacement.Epoch > empty.Epoch);
        Assert.Equal(4, replacement.Health!.Current);
        Assert.Equal(Rac1HudProjection.WrenchPresentationKey, replacement.CurrentWeapon!.PresentationKey);
        Assert.Empty(replacement.Feedback);
    }

    [Fact]
    public void BombGlovePickupFeedbackReportsEffectiveAmmoDelta()
    {
        HudFeedbackDraft feedback = Rac1HudProjection.BombGloveAmmoPickupFeedback(2);

        Assert.Equal(HudFeedbackKind.Pickup, feedback.Kind);
        Assert.Equal(Rac1HudProjection.BombGloveAmmoResourceKey, feedback.ResourceKey);
        Assert.Equal(2, feedback.Delta);
        Assert.Equal(Rac1HudProjection.BombGloveNameKey, feedback.SubjectKey);
    }

    [Fact]
    public void BoltPickupFeedbackDoesNotInventAPlayerWallet()
    {
        var publisher = new HudStateAdapter();
        var state = Rac1HudProjection.Capture(
            new Rac1RatchetNanotechSession(),
            new Rac1WeaponInventory(true, firstRangedAmmo: 0));

        publisher.BeginSession(state);
        var pickup = publisher.Publish(
            state,
            Rac1HudProjection.CollectedBoltFeedback(5));

        Assert.Null(pickup.Bolts);
        var feedback = Assert.Single(pickup.Feedback);
        Assert.Equal(HudFeedbackKind.Pickup, feedback.Kind);
        Assert.Equal(Rac1HudProjection.BoltsResourceKey, feedback.ResourceKey);
        Assert.Equal(5, feedback.Delta);
    }
}
