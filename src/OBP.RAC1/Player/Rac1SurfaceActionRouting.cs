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
    /// recovered. The later water-family recovery resolves shallow-water WADE as
    /// state 0x72; execution still remains separate until its motion law is recovered.
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
            Rac1SurfaceInteractionKind.ShallowWaterWade =>
                new(
                    Rac1SurfaceInteractionKind.ShallowWaterWade,
                    Rac1PlayerActionDomain.Wade),
            Rac1SurfaceInteractionKind.None => null,
            _ => throw new ArgumentOutOfRangeException(nameof(contact)),
        };
    }
}
