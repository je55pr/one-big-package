namespace OBP.Runtime.Gameplay;

/// <summary>
/// Neutral lifetime visibility proven across admitted gameplay examples.
/// Source-game state numbers and reasons for deactivation do not cross here.
/// </summary>
public enum RuntimeEntityPresence
{
    Active,
    Inactive,
}

/// <summary>
/// Stable authored identity for one gameplay entity. This is copied from the
/// immutable <see cref="RuntimeDynamicObject"/> definition and never mutates.
/// </summary>
public sealed record RuntimeEntityIdentity(
    string SourceGame,
    string InteractionId,
    int NativeClassId,
    int InstanceIndex,
    int? NativeUid)
{
    public static RuntimeEntityIdentity From(RuntimeDynamicObject source) =>
        new(source.SourceGame, source.InteractionId, source.NativeClassId,
            source.InstanceIndex, source.NativeUid);

    public bool Matches(RuntimeDynamicObject source) => this == From(source);
}

/// <summary>
/// Game-neutral presentation/lifetime state for one live entity. This is a
/// snapshot that can be replaced over time; authored payload bytes never enter it.
/// </summary>
public sealed record RuntimeEntityPresentationState(
    RuntimeObjectTransform Transform,
    RuntimeEntityPresence Presence = RuntimeEntityPresence.Active,
    RuntimeObjectAnimationRole AnimationRole = RuntimeObjectAnimationRole.Rest);

/// <summary>
/// Current live snapshot paired with immutable authored identity. Source-game
/// controllers interpret native/PVar state and emit only these neutral effects.
/// </summary>
public sealed record RuntimeEntityState(
    RuntimeEntityIdentity Identity,
    RuntimeEntityPresentationState Presentation)
{
    public static RuntimeEntityState FromAuthored(RuntimeDynamicObject source) =>
        new(RuntimeEntityIdentity.From(source), new RuntimeEntityPresentationState(
            source.Transform, AnimationRole: source.Animations?.InitialRole ?? RuntimeObjectAnimationRole.Rest));

    public RuntimeEntityState WithPresence(RuntimeEntityPresence presence) =>
        this with { Presentation = Presentation with { Presence = presence } };

    public RuntimeEntityState WithAnimationRole(RuntimeObjectAnimationRole role) =>
        this with { Presentation = Presentation with { AnimationRole = role } };

    public RuntimeEntityState WithTransform(RuntimeObjectTransform transform) =>
        this with { Presentation = Presentation with { Transform = transform } };

    public void EnsureMatches(RuntimeDynamicObject source)
    {
        if (!Identity.Matches(source))
        {
            throw new InvalidOperationException(
                $"Entity state {Identity.InteractionId} does not belong to {source.InteractionId}.");
        }
    }
}
