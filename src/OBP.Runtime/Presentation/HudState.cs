namespace OBP.Runtime.Presentation;

public enum HudLifeState
{
    Alive,
    Dead,
}

public enum HudFeedbackKind
{
    Pickup,
    Damage,
}

public sealed record HudHealth(
    int Current,
    int? Capacity,
    string UnitKey,
    HudLifeState? LifeState = null);

public sealed record HudCurrency(
    string CurrencyKey,
    int Balance);

public sealed record HudAmmo(
    int Current,
    int? Capacity = null);
public sealed record HudWeapon(
    string PresentationKey,
    string? NameKey = null,
    HudAmmo? Ammo = null);

public sealed record HudContextPrompt(
    string PromptId,
    string ActionId,
    string MessageKey,
    string? SubjectKey = null,
    double? Progress = null);

public sealed record HudFeedbackDraft(
    HudFeedbackKind Kind,
    string? ResourceKey = null,
    int? Delta = null,
    string? SubjectKey = null);

public sealed record HudFeedbackEvent(
    long EventId,
    HudFeedbackKind Kind,
    string? ResourceKey = null,
    int? Delta = null,
    string? SubjectKey = null);
public sealed record HudPresentationState(
    HudHealth? Health = null,
    HudCurrency? Bolts = null,
    HudWeapon? CurrentWeapon = null,
    HudContextPrompt? ContextPrompt = null);

public sealed record HudSnapshot(
    long Epoch,
    long Revision,
    HudHealth? Health,
    HudCurrency? Bolts,
    HudWeapon? CurrentWeapon,
    IReadOnlyList<HudFeedbackEvent> Feedback,
    HudContextPrompt? ContextPrompt)
{
    public static HudSnapshot Empty { get; } = new(
        Epoch: 0,
        Revision: 0,
        Health: null,
        Bolts: null,
        CurrentWeapon: null,
        Feedback: Array.Empty<HudFeedbackEvent>(),
        ContextPrompt: null);
}
/// <summary>
/// Application-facing lifecycle adapter for engine-independent HUD state.
/// Gameplay systems remain authoritative; this class only stamps immutable
/// projections with session epochs, revisions and transient event identities.
/// </summary>
public sealed class HudStateAdapter
{
    private long _epoch;
    private long _revision;
    private long _nextEventId = 1;
    private bool _sessionStarted;

    public HudSnapshot Current { get; private set; } = HudSnapshot.Empty;

    public HudSnapshot BeginSession(HudPresentationState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _epoch = checked(_epoch + 1);
        _revision = 0;
        _nextEventId = 1;
        _sessionStarted = true;
        Current = Build(state, Array.Empty<HudFeedbackEvent>());
        return Current;
    }
    public HudSnapshot ResetSession() => BeginSession(new HudPresentationState());

    public HudSnapshot Publish(
        HudPresentationState state,
        params HudFeedbackDraft[] feedback)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(feedback);
        if (!_sessionStarted)
            throw new InvalidOperationException("BeginSession must be called before publishing HUD state.");

        _revision = checked(_revision + 1);
        var events = feedback
            .Select(item => new HudFeedbackEvent(
                EventId: _nextEventId++,
                Kind: item.Kind,
                ResourceKey: item.ResourceKey,
                Delta: item.Delta,
                SubjectKey: item.SubjectKey))
            .ToArray();

        Current = Build(state, events);
        return Current;
    }

    private HudSnapshot Build(
        HudPresentationState state,
        IReadOnlyList<HudFeedbackEvent> feedback) =>
        new(
            Epoch: _epoch,
            Revision: _revision,
            Health: state.Health,
            Bolts: state.Bolts,
            CurrentWeapon: state.CurrentWeapon,
            Feedback: feedback,
            ContextPrompt: state.ContextPrompt);
}
