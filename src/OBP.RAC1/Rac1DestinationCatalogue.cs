using OBP.Core;

namespace OBP.RAC1;

/// <summary>
/// Host-facing catalogue for the 19 retail-validated native R&C1 level ids.
/// Human display names are the canonical decoded destination names; native LEVEL#
/// identity remains separate for routing, diagnostics and serialization.
/// </summary>
public sealed class Rac1DestinationCatalogue : IObpDestinationCatalogue
{
    public static readonly Rac1DestinationCatalogue Instance = new();

    private static readonly string[] DisplayNames =
    [
        "Veldin", "Novalis", "Aridia", "Kerwan", "Eudora", "Rilgar", "Nebula G34", "Umbris", "Batalia", "Gaspar",
        "Orxon", "Pokitaru", "Hoven", "Oltanis Orbit", "Oltanis", "Quartu", "Kalebo III", "Veldin Orbit", "Veldin",
    ];

    private static readonly IReadOnlyList<ObpDestination> Entries = Array.AsReadOnly(
        Enumerable.Range(0, DisplayNames.Length).Select(ToDestination).ToArray());

    private Rac1DestinationCatalogue() { }

    public ObpSourceGame Game => ObpSourceGame.Rac1;
    public string BuildId => Rac1Authority.Primary.BuildId;
    public IReadOnlyList<ObpDestination> Destinations => Entries;

    public ObpDestination? FindByNativeLevel(int levelId) =>
        Entries.FirstOrDefault(destination => destination.NativeDestinationId == $"LEVEL{levelId}");

    public static string? ResolveDisplayName(int levelId) =>
        (uint)levelId < (uint)DisplayNames.Length ? DisplayNames[levelId] : null;

    private static ObpDestination ToDestination(int levelId) => new(
        DestinationId: $"rac1:LEVEL{levelId}",
        Game: ObpSourceGame.Rac1,
        BuildId: Rac1Authority.Primary.BuildId,
        NativeDestinationId: $"LEVEL{levelId}",
        NativeEngineId: null,
        PlanetLabel: DisplayNames[levelId],
        LocationLabel: null,
        NativeContainer: null,
        Kind: ObpDestinationKind.Planet);
}
