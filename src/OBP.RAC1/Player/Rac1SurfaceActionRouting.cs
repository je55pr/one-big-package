namespace OBP.RAC1.Player;

/// <summary>
/// Evidence-backed request for a player action selected from recovered contact
/// semantics. Selection is separate from action execution because the alternate
/// movement/update controllers for these states are not yet implemented.
/// </summary>
public sealed record Rac1SurfaceActionIntent(
    Rac1SurfaceInteractionKind Interaction,
    int NativeActionState);

public static class Rac1SurfaceActionRouting
{
    /// <summary>
    /// Maps only contact classes whose native player action is independently
    /// recovered. Shallow water deliberately remains unresolved here because the
    /// retained evidence disagrees on its exact action slot.
    /// </summary>
    public static Rac1SurfaceActionIntent? Select(Rac1PlayerContactResult contact)
    {
        ArgumentNullException.ThrowIfNull(contact);

        return contact.SurfaceInteraction switch
        {
            Rac1SurfaceInteractionKind.IceSlide =>
                new(
                    Rac1SurfaceInteractionKind.IceSlide,
                    Rac1PlayerActionDomain.IceSlide),
            Rac1SurfaceInteractionKind.MudSink =>
                new(
                    Rac1SurfaceInteractionKind.MudSink,
                    Rac1PlayerActionDomain.Mud),
            Rac1SurfaceInteractionKind.MagnebootSupport =>
                new(
                    Rac1SurfaceInteractionKind.MagnebootSupport,
                    Rac1PlayerActionDomain.Magneboot),
            Rac1SurfaceInteractionKind.None or
            Rac1SurfaceInteractionKind.ShallowWaterWade => null,
            _ => throw new ArgumentOutOfRangeException(nameof(contact)),
        };
    }
}
