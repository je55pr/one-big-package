namespace OBP.Core;

/// <summary>
/// Coarse host-facing classification for a native destination. This is for
/// browsing/debug presentation only; it does not prescribe OBP campaign order,
/// global planet identity or unlock rules.
/// </summary>
public enum ObpDestinationKind
{
    Planet,
    Hub,
    Vendor,
    SpaceCombat,
    Scene,
    Unresolved,
}

/// <summary>
/// One source-game destination exposed to the OBP host. Native destination
/// identity is deliberately separate from the human planet/location labels:
/// recurring places across games (or multiple levels for one planet inside a
/// game) must not be collapsed merely because their display names match.
/// </summary>
public sealed record ObpDestination(
    string DestinationId,
    ObpSourceGame Game,
    string BuildId,
    string NativeDestinationId,
    string? NativeEngineId,
    string PlanetLabel,
    string? LocationLabel,
    string? NativeContainer,
    ObpDestinationKind Kind)
{
    public string DisplayName => string.IsNullOrWhiteSpace(LocationLabel)
        ? PlanetLabel
        : $"{PlanetLabel} — {LocationLabel}";

    public bool Walkable => Kind is ObpDestinationKind.Planet or ObpDestinationKind.Hub or ObpDestinationKind.Vendor;
}

/// <summary>
/// Read-only destination discovery contract. Import/loading is intentionally not
/// part of this interface: source availability, destination discovery and a
/// production world provider are separate capabilities during reconstruction.
/// </summary>
public interface IObpDestinationCatalogue
{
    ObpSourceGame Game { get; }
    string BuildId { get; }
    IReadOnlyList<ObpDestination> Destinations { get; }
}
