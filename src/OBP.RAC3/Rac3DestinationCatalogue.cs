using OBP.Core;

namespace OBP.RAC3;

/// <summary>
/// Host-facing catalogue for the 51 retail-observed UYA main-level table rows.
/// Human planet/kind semantics remain unresolved here; sparse physical table
/// identity is retained instead of pretending the table is a dense level list.
/// </summary>
public sealed class Rac3DestinationCatalogue : IObpDestinationCatalogue
{
    public static readonly Rac3DestinationCatalogue Instance = new();
    public static IReadOnlyList<int> ObservedMainTableIndices { get; } = Array.AsReadOnly(new[]
    {
        1,2,3,4,5,6,7,8,9,10,11,12,13,14,16,17,18,19,20,21,22,23,24,26,27,28,29,30,31,32,33,34,35,36,
        39,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,
    });
    private static readonly IReadOnlyList<ObpDestination> Entries = Array.AsReadOnly(ObservedMainTableIndices.Select(ToDestination).ToArray());
    private Rac3DestinationCatalogue() { }
    public ObpSourceGame Game => ObpSourceGame.Rac3;
    public string BuildId => Rac3Authority.Primary.BuildId;
    public IReadOnlyList<ObpDestination> Destinations => Entries;
    public ObpDestination? FindByTableIndex(int tableIndex) => Entries.FirstOrDefault(d => d.NativeDestinationId == $"TABLE{tableIndex}");
    private static ObpDestination ToDestination(int tableIndex) => new(
        DestinationId: $"rac3:TABLE{tableIndex}", Game: ObpSourceGame.Rac3, BuildId: Rac3Authority.Primary.BuildId,
        NativeDestinationId: $"TABLE{tableIndex}", NativeEngineId: null, PlanetLabel: $"UYA table {tableIndex}", LocationLabel: null,
        NativeContainer: "retail-observed resident sparse table", Kind: ObpDestinationKind.Unresolved);
}
