using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

public sealed class HudTransientFeedbackTests
{
    [Fact]
    public void DamageAndPickupEventsProduceBoundedDeterministicCues()
    {
        var adapter = new HudStateAdapter();
        var animator = new HudTransientFeedbackAnimator();
        var initial = adapter.BeginSession(State(health: 4));

        animator.Accept(initial);
        var changed = adapter.Publish(
            State(health: 3),
            new HudFeedbackDraft(
                HudFeedbackKind.Damage,
                ResourceKey: "nanotech",
                Delta: -1),
            new HudFeedbackDraft(
                HudFeedbackKind.Pickup,
                ResourceKey: "bolts",
                Delta: 5));

        HudTransientFeedbackState active = animator.Accept(changed);

        Assert.Equal(1d, active.HealthPulse);
        Assert.Equal(-1, active.HealthDelta);
        Assert.Equal(1d, active.PickupPulse);
        Assert.Equal("bolts", active.PickupResourceKey);
        Assert.Equal(5, active.PickupDelta);

        HudTransientFeedbackState expired = animator.Advance(
            HudTransientFeedbackAnimator.PickupPulseSeconds + 0.01d);

        Assert.Equal(0d, expired.HealthPulse);
        Assert.Null(expired.HealthDelta);
        Assert.Equal(0d, expired.PickupPulse);
        Assert.Null(expired.PickupDelta);

        HudTransientFeedbackState recovered = animator.Accept(
            adapter.Publish(State(health: 4)));

        Assert.Equal(1d, recovered.HealthPulse);
        Assert.Equal(1, recovered.HealthDelta);
    }

    [Fact]
    public void RepeatedRevisionDoesNotRestartAndNewEpochClearsTransientState()
    {
        var adapter = new HudStateAdapter();
        var animator = new HudTransientFeedbackAnimator();
        animator.Accept(adapter.BeginSession(State(health: 4)));

        HudSnapshot damage = adapter.Publish(
            State(health: 3),
            new HudFeedbackDraft(HudFeedbackKind.Damage, Delta: -1));
        animator.Accept(damage);
        HudTransientFeedbackState halfway = animator.Advance(
            HudTransientFeedbackAnimator.HealthPulseSeconds / 2d);

        HudTransientFeedbackState repeated = animator.Accept(damage);

        Assert.Equal(halfway.HealthPulse, repeated.HealthPulse);
        Assert.Equal(-1, repeated.HealthDelta);

        HudTransientFeedbackState reset = animator.Accept(
            adapter.BeginSession(State(health: 2)));

        Assert.Equal(0d, reset.HealthPulse);
        Assert.Null(reset.HealthDelta);
        Assert.Equal(0d, reset.PickupPulse);
        Assert.Equal(0d, reset.WeaponPulse);
    }

    [Fact]
    public void WeaponSelectionAndAmmoConsumptionPulseFromAuthoritativeStateChanges()
    {
        var nanotech = new Rac1RatchetNanotechSession();
        var weapons = new Rac1WeaponInventory(
            ownsFirstRanged: true,
            firstRangedAmmo: 2);
        var adapter = new HudStateAdapter();
        var animator = new HudTransientFeedbackAnimator();

        animator.Accept(adapter.BeginSession(
            Rac1HudProjection.Capture(nanotech, weapons)));

        Assert.True(weapons.TryEquip(Rac1WeaponId.FirstRanged));
        HudTransientFeedbackState equipped = animator.Accept(
            adapter.Publish(Rac1HudProjection.Capture(nanotech, weapons)));

        Assert.Equal(1d, equipped.WeaponPulse);
        Assert.Null(equipped.AmmoDelta);

        animator.Advance(HudTransientFeedbackAnimator.WeaponPulseSeconds);
        Assert.True(weapons.TryUseEquipped());
        HudTransientFeedbackState fired = animator.Accept(
            adapter.Publish(Rac1HudProjection.Capture(nanotech, weapons)));

        Assert.Equal(1d, fired.WeaponPulse);
        Assert.Equal(-1, fired.AmmoDelta);
    }

    [Fact]
    public void ContextPromptFadesInAndOutWithoutChangingGameplayState()
    {
        var adapter = new HudStateAdapter();
        var animator = new HudTransientFeedbackAnimator();
        var prompt = new HudContextPrompt(
            PromptId: "console",
            ActionId: "interact",
            MessageKey: "Activate console");

        HudSnapshot shown = adapter.BeginSession(
            State(health: 4) with { ContextPrompt = prompt });
        HudTransientFeedbackState entered = animator.Accept(shown);

        Assert.Equal(0d, entered.PromptOpacity);
        Assert.Equal(1d, entered.PromptPulse);

        HudTransientFeedbackState visible = animator.Advance(
            HudTransientFeedbackAnimator.PromptFadeSeconds);
        Assert.Equal(1d, visible.PromptOpacity);

        animator.Accept(adapter.Publish(State(health: 4)));
        HudTransientFeedbackState hidden = animator.Advance(
            HudTransientFeedbackAnimator.PromptFadeSeconds);

        Assert.Equal(0d, hidden.PromptOpacity);
    }

    [Fact]
    public void EmptyReplacementEpochClearsUnsupportedGameTransientState()
    {
        var adapter = new HudStateAdapter();
        var animator = new HudTransientFeedbackAnimator();

        animator.Accept(adapter.BeginSession(State(health: 4)));
        animator.Accept(adapter.Publish(
            State(health: 3),
            new HudFeedbackDraft(HudFeedbackKind.Damage, "nanotech", -1),
            new HudFeedbackDraft(HudFeedbackKind.Pickup, "bolts", 5)));

        HudSnapshot unsupported = adapter.ResetSession();
        HudTransientFeedbackState reset = animator.Accept(unsupported);

        Assert.Null(unsupported.Health);
        Assert.Null(unsupported.Bolts);
        Assert.Null(unsupported.CurrentWeapon);
        Assert.Null(unsupported.ContextPrompt);
        Assert.Empty(unsupported.Feedback);
        Assert.Equal(0d, reset.HealthPulse);
        Assert.Null(reset.HealthDelta);
        Assert.Equal(0d, reset.PickupPulse);
        Assert.Null(reset.PickupDelta);
        Assert.Equal(0d, reset.WeaponPulse);
        Assert.Equal(0d, reset.PromptOpacity);
    }

    private static HudPresentationState State(int health) => new(
        Health: new HudHealth(
            Current: health,
            Capacity: null,
            UnitKey: "nanotech",
            LifeState: health > 0 ? HudLifeState.Alive : HudLifeState.Dead),
        Bolts: null,
        CurrentWeapon: new HudWeapon(
            PresentationKey: "weapon.wrench",
            NameKey: "weapon.wrench"),
        ContextPrompt: null);
}
