using OBP.Core;

namespace OBP.RAC1;

/// <summary>
/// Neutral host-facing catalogue for the 19 retail-validated native R&C1 level
/// ids. Planet/location names and runtime engine ids stay unresolved until they
/// are independently mapped from retail/executable evidence.
/// </summary>
public sealed class Rac1DestinationCatalogue : IObpDestinationCatalogue
{
    public static readonly Rac1DestinationCatalogue Instance = new();

    private static readonly IReadOnlyList<ObpDestination> Entries = Array.AsReadOnly(
        Enumerable.Range(0, 19).Select(ToDestination).ToArray());

    private Rac1DestinationCatalogue() { }

    public ObpSourceGame Game => ObpSourceGame.Rac1;
    public string BuildId => Rac1Authority.Primary.BuildId;
    public IReadOnlyList<ObpDestination> Destinations => Entries;

    public ObpDestination? FindByNativeLevel(int levelId) =>
        Entries.FirstOrDefault(destination => destination.NativeDestinationId == $"LEVEL{levelId}");

    private static ObpDestination ToDestination(int levelId) => new(
        DestinationId: $"rac1:LEVEL{levelId}",
        Game: ObpSourceGame.Rac1,
        BuildId: Rac1Authority.Primary.BuildId,
        NativeDestinationId: $"LEVEL{levelId}",
        NativeEngineId: null,
        PlanetLabel: $"R&C1 LEVEL{levelId}",
        LocationLabel: null,
        NativeContainer: null,
        Kind: ObpDestinationKind.Unresolved);
}
