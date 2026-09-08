using OBP.Core;

namespace OBP.RAC2;

/// <summary>
/// Neutral host-facing projection of <see cref="GcPlanetCatalogue"/>. The native
/// GC catalogue remains the authority for GC-specific level identity; this
/// adapter prevents the application layer from needing GC enum/record types.
/// </summary>
public sealed class GcDestinationCatalogue : IObpDestinationCatalogue
{
    public static readonly GcDestinationCatalogue Instance = new();

    private static readonly IReadOnlyList<ObpDestination> Entries = Array.AsReadOnly(
        GcPlanetCatalogue.All.Select(ToDestination).ToArray());

    private GcDestinationCatalogue() { }

    public ObpSourceGame Game => ObpSourceGame.Rac2;
    public string BuildId => Rac2Authority.Primary.BuildId;
    public IReadOnlyList<ObpDestination> Destinations => Entries;

    public ObpDestination? FindByNativeLevel(int levelId) =>
        Entries.FirstOrDefault(d => d.NativeDestinationId == $"LEVEL{levelId}");

    private static ObpDestination ToDestination(GcPlanetCatalogue.Entry entry) => new(
        DestinationId: $"rac2:LEVEL{entry.LevelId}",
        Game: ObpSourceGame.Rac2,
        BuildId: Rac2Authority.Primary.BuildId,
        NativeDestinationId: $"LEVEL{entry.LevelId}",
        // LEVEL21.WAD self-reports engine id 30. Preserve both facts rather
        // than making a generic destination id pretend the file/index and
        // runtime engine id are always equal.
        NativeEngineId: entry.LevelId == 21 ? "30" : entry.LevelId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        PlanetLabel: entry.Planet,
        LocationLabel: entry.Location,
        NativeContainer: entry.ContainerFile,
        Kind: entry.Kind switch
        {
            GcLevelKind.Planet => ObpDestinationKind.Planet,
            GcLevelKind.Hub => ObpDestinationKind.Hub,
            GcLevelKind.Vendor => ObpDestinationKind.Vendor,
            GcLevelKind.SpaceCombat => ObpDestinationKind.SpaceCombat,
            GcLevelKind.Scene => ObpDestinationKind.Scene,
            GcLevelKind.Unresolved => ObpDestinationKind.Unresolved,
            _ => throw new ArgumentOutOfRangeException(nameof(entry.Kind), entry.Kind, "Unknown GC destination kind."),
        });
}
