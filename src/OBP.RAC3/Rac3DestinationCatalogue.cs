using OBP.Core;

namespace OBP.RAC3;

/// <summary>
/// Host-facing catalogue for the 51 retail-observed UYA main-level table rows.
/// Human labels come from the repository's public level-name cross-reference;
/// sparse TABLE# identity remains canonical for routing and diagnostics.
/// </summary>
public sealed class Rac3DestinationCatalogue : IObpDestinationCatalogue
{
    public static readonly Rac3DestinationCatalogue Instance = new();

    private sealed record NamedRow(int TableIndex, string DisplayName);

    private static readonly NamedRow[] NamedRows =
    [
        new(1, "Veldin"),
        new(2, "Florana"),
        new(3, "Starship Phoenix"),
        new(4, "Marcadia"),
        new(5, "Daxx"),
        new(6, "Starship Phoenix (Under Attack)"),
        new(7, "Annihilation Nation"),
        new(8, "Aquatos"),
        new(9, "Tyhrranosis"),
        new(10, "Zeldrin Starport"),
        new(11, "Obani Moons"),
        new(12, "Rilgar"),
        new(13, "Holostar Studios Ratchet"),
        new(14, "Koros"),
        new(16, "Kerwan"),
        new(17, "Crash Site"),
        new(18, "Aridia"),
        new(19, "Thran Asteroid Belt"),
        new(20, "Final Boss"),
        new(21, "Obani Draco"),
        new(22, "Mylon"),
        new(23, "Holostar Studios Clank"),
        new(24, "Insomniac Museum"),
        new(26, "Kerwan Ranger Missions"),
        new(27, "Aquatos Base"),
        new(28, "Aquatos Sewers"),
        new(29, "Tyhrranosis Ranger Missions"),
        // The cross-reference calls row 30 "Vid-Comic Unnamed"; omit that placeholder qualifier from ordinary display.
        new(30, "Vid-Comic"),
        new(31, "Vid-Comic 1"),
        new(32, "Vid-Comic 4"),
        new(33, "Vid-Comic 2"),
        new(34, "Vid-Comic 3"),
        new(35, "Vid-Comic 5"),
        new(36, "Vid-Comic 1 Special Edition"),
        new(39, "Multiplayer Menu"),
        new(40, "Bakisi Isles"),
        new(41, "Hoven Gorge"),
        new(42, "Outpost X12"),
        new(43, "Korgon Outpost"),
        new(44, "Metropolis"),
        new(45, "Blackwater City"),
        new(46, "Command Center"),
        new(47, "Blackwater Docks"),
        new(48, "Aquatos Sewers"),
        new(49, "Marcadia Palace"),
        new(50, "Bakisi Isles (Split-screen)"),
        new(51, "Hoven Gorge (Split-screen)"),
        new(52, "Outpost X12 (Split-screen)"),
        new(53, "Korgon Outpost (Split-screen)"),
        new(54, "Metropolis (Split-screen)"),
        new(55, "Blackwater City (Split-screen)"),
    ];

    public static IReadOnlyList<int> ObservedMainTableIndices { get; } =
        Array.AsReadOnly(NamedRows.Select(row => row.TableIndex).ToArray());

    private static readonly IReadOnlyList<ObpDestination> Entries =
        Array.AsReadOnly(NamedRows.Select(ToDestination).ToArray());

    private Rac3DestinationCatalogue() { }
    public ObpSourceGame Game => ObpSourceGame.Rac3;
    public string BuildId => Rac3Authority.Primary.BuildId;
    public IReadOnlyList<ObpDestination> Destinations => Entries;

    public ObpDestination? FindByTableIndex(int tableIndex) =>
        Entries.FirstOrDefault(destination => destination.NativeDestinationId == $"TABLE{tableIndex}");

    private static ObpDestination ToDestination(NamedRow row) => new(
        DestinationId: $"rac3:TABLE{row.TableIndex}",
        Game: ObpSourceGame.Rac3,
        BuildId: Rac3Authority.Primary.BuildId,
        NativeDestinationId: $"TABLE{row.TableIndex}",
        NativeEngineId: null,
        PlanetLabel: row.DisplayName,
        LocationLabel: null,
        NativeContainer: "retail-observed resident sparse table",
        Kind: ObpDestinationKind.Unresolved);
}
