using System.Globalization;
using OBP.Core;
using OBP.IO;
using OBP.Runtime;

namespace OBP.RAC3;

/// <summary>UYA implementation of the neutral runtime-world provider.</summary>
public sealed class Rac3WorldProvider : IObpWorldProvider
{
    public static readonly Rac3WorldProvider Instance = new();
    private Rac3WorldProvider() { }
    public ObpSourceGame Game => ObpSourceGame.Rac3;
    public string BuildId => Rac3Authority.Primary.BuildId;
    public IObpDestinationCatalogue Catalogue => Rac3DestinationCatalogue.Instance;
    public bool CanLoad(ObpDestination destination) => destination.Game == Game && destination.BuildId == BuildId && ResolveTableIndex(destination) is not null;
    public RuntimeWorld Load(string sourcePath, ObpDestination destination)
    {
        int table = ResolveTableIndex(destination) ?? throw new ArgumentException($"Destination '{destination.DestinationId}' is not loadable by the UYA provider.", nameof(destination));
        using var reader = new FileRandomAccessReader(sourcePath);
        return Rac3WorldImport.Build(reader, table);
    }
    public int? ResolveTableIndex(ObpDestination destination)
    {
        if (destination.Game != Game || destination.BuildId != BuildId || !destination.NativeDestinationId.StartsWith("TABLE", StringComparison.OrdinalIgnoreCase)) return null;
        if (!int.TryParse(destination.NativeDestinationId.AsSpan(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out int table)) return null;
        var canonical = Rac3DestinationCatalogue.Instance.FindByTableIndex(table);
        return canonical is not null && canonical.DestinationId == destination.DestinationId ? table : null;
    }
}
