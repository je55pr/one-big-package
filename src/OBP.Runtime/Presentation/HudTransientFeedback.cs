namespace OBP.Runtime.Presentation;

public sealed record HudTransientFeedbackState(
    double HealthPulse,
    int? HealthDelta,
    double PickupPulse,
    string? PickupResourceKey,
    int? PickupDelta,
    double WeaponPulse,
    int? AmmoDelta,
    double PromptOpacity,
    double PromptPulse)
{
    public static HudTransientFeedbackState Empty { get; } = new(
        HealthPulse: 0d,
        HealthDelta: null,
        PickupPulse: 0d,
        PickupResourceKey: null,
        PickupDelta: null,
        WeaponPulse: 0d,
        AmmoDelta: null,
        PromptOpacity: 0d,
        PromptPulse: 0d);
}

/// <summary>
/// Deterministic presentation-only transients derived from HUD snapshots.
/// Gameplay remains authoritative: this class observes revisions and explicit
/// feedback events, then supplies short-lived visual intensities to a renderer.
/// </summary>
public sealed class HudTransientFeedbackAnimator
{
    public const double HealthPulseSeconds = 0.28d;
    public const double PickupPulseSeconds = 0.42d;
    public const double WeaponPulseSeconds = 0.30d;
    public const double PromptFadeSeconds = 0.18d;
    public const double PromptPulseSeconds = 0.24d;

    private long _epoch = -1;
    private long _revision = -1;
    private HudSnapshot? _previous;
    private double _healthRemaining;
    private int? _healthDelta;
    private double _pickupRemaining;
    private string? _pickupResourceKey;
    private int? _pickupDelta;
    private double _weaponRemaining;
    private int? _ammoDelta;
    private bool _promptTargetVisible;
    private double _promptOpacity;
    private double _promptPulseRemaining;

    public HudTransientFeedbackState Current => new(
        HealthPulse: Normalized(_healthRemaining, HealthPulseSeconds),
        HealthDelta: _healthRemaining > 0d ? _healthDelta : null,
        PickupPulse: Normalized(_pickupRemaining, PickupPulseSeconds),
        PickupResourceKey: _pickupRemaining > 0d ? _pickupResourceKey : null,
        PickupDelta: _pickupRemaining > 0d ? _pickupDelta : null,
        WeaponPulse: Normalized(_weaponRemaining, WeaponPulseSeconds),
        AmmoDelta: _weaponRemaining > 0d ? _ammoDelta : null,
        PromptOpacity: _promptOpacity,
        PromptPulse: Normalized(_promptPulseRemaining, PromptPulseSeconds));

    public HudTransientFeedbackState Accept(HudSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Epoch < _epoch ||
            (snapshot.Epoch == _epoch && snapshot.Revision <= _revision))
        {
            return Current;
        }

        if (snapshot.Epoch != _epoch)
        {
            ResetForEpoch(snapshot);
            return Current;
        }

        ObserveTransition(_previous!, snapshot);
        _revision = snapshot.Revision;
        _previous = snapshot;
        return Current;
    }

    public HudTransientFeedbackState Advance(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds))
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));

        double delta = Math.Max(0d, deltaSeconds);
        _healthRemaining = Decay(_healthRemaining, delta);
        _pickupRemaining = Decay(_pickupRemaining, delta);
        _weaponRemaining = Decay(_weaponRemaining, delta);
        _promptPulseRemaining = Decay(_promptPulseRemaining, delta);

        double promptStep = PromptFadeSeconds <= 0d
            ? 1d
            : delta / PromptFadeSeconds;
        _promptOpacity = MoveToward(
            _promptOpacity,
            _promptTargetVisible ? 1d : 0d,
            promptStep);
        return Current;
    }

    private void ResetForEpoch(HudSnapshot snapshot)
    {
        _epoch = snapshot.Epoch;
        _revision = snapshot.Revision;
        _previous = snapshot;
        _healthRemaining = 0d;
        _healthDelta = null;
        _pickupRemaining = 0d;
        _pickupResourceKey = null;
        _pickupDelta = null;
        _weaponRemaining = 0d;
        _ammoDelta = null;
        _promptTargetVisible = snapshot.ContextPrompt is not null;
        _promptOpacity = 0d;
        _promptPulseRemaining = _promptTargetVisible ? PromptPulseSeconds : 0d;
    }

    private void ObserveTransition(HudSnapshot before, HudSnapshot after)
    {
        HudFeedbackEvent? damage = after.Feedback
            .LastOrDefault(item => item.Kind == HudFeedbackKind.Damage && item.Delta is < 0);
        int? healthDelta = damage?.Delta;
        if (healthDelta is null &&
            before.Health is { } oldHealth &&
            after.Health is { } newHealth &&
            oldHealth.Current != newHealth.Current)
        {
            healthDelta = checked(newHealth.Current - oldHealth.Current);
        }

        if (healthDelta is not null)
        {
            _healthDelta = healthDelta;
            _healthRemaining = HealthPulseSeconds;
        }

        HudFeedbackEvent? pickup = after.Feedback
            .LastOrDefault(item => item.Kind == HudFeedbackKind.Pickup && item.Delta is not null);
        if (pickup is not null)
        {
            StartPickup(pickup.ResourceKey, pickup.Delta!.Value);
        }
        else if (before.Bolts is { } oldBolts &&
                 after.Bolts is { } newBolts &&
                 oldBolts.CurrencyKey == newBolts.CurrencyKey &&
                 oldBolts.Balance != newBolts.Balance)
        {
            StartPickup(
                newBolts.CurrencyKey,
                checked(newBolts.Balance - oldBolts.Balance));
        }

        string? oldWeapon = before.CurrentWeapon?.PresentationKey;
        string? newWeapon = after.CurrentWeapon?.PresentationKey;
        if (oldWeapon != newWeapon)
        {
            _weaponRemaining = newWeapon is null ? 0d : WeaponPulseSeconds;
            _ammoDelta = null;
        }
        else if (before.CurrentWeapon?.Ammo is { } oldAmmo &&
                 after.CurrentWeapon?.Ammo is { } newAmmo &&
                 oldAmmo.Current != newAmmo.Current)
        {
            _ammoDelta = checked(newAmmo.Current - oldAmmo.Current);
            _weaponRemaining = WeaponPulseSeconds;
        }

        bool promptChanged = !PromptIdentityEquals(
            before.ContextPrompt,
            after.ContextPrompt);
        _promptTargetVisible = after.ContextPrompt is not null;
        if (promptChanged && _promptTargetVisible)
        {
            _promptPulseRemaining = PromptPulseSeconds;
        }
    }

    private void StartPickup(string? resourceKey, int delta)
    {
        _pickupResourceKey = resourceKey;
        _pickupDelta = delta;
        _pickupRemaining = PickupPulseSeconds;
    }

    private static bool PromptIdentityEquals(
        HudContextPrompt? left,
        HudContextPrompt? right) =>
        left?.PromptId == right?.PromptId &&
        left?.ActionId == right?.ActionId &&
        left?.MessageKey == right?.MessageKey &&
        left?.SubjectKey == right?.SubjectKey;

    private static double Decay(double remaining, double delta) =>
        Math.Max(0d, remaining - delta);

    private static double Normalized(double remaining, double duration) =>
        duration <= 0d ? 0d : Math.Clamp(remaining / duration, 0d, 1d);

    private static double MoveToward(double value, double target, double amount)
    {
        if (value < target)
            return Math.Min(target, value + amount);
        if (value > target)
            return Math.Max(target, value - amount);
        return value;
    }
}
