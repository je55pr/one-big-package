using System.Globalization;
using OBP.Core;
using OBP.IO;
using OBP.Runtime;

namespace OBP.RAC2;

/// <summary>
/// Going Commando implementation of the neutral runtime-world provider. It is a
/// very small adapter: native GC identity remains in <see cref="GcDestinationCatalogue"/>
/// and decoding remains in <see cref="GcWorldImport"/>.
/// </summary>
public sealed class GcWorldProvider : IObpWorldProvider
{
    public static readonly GcWorldProvider Instance = new();

    private GcWorldProvider() { }

    public ObpSourceGame Game => ObpSourceGame.Rac2;
    public string BuildId => Rac2Authority.Primary.BuildId;
    public IObpDestinationCatalogue Catalogue => GcDestinationCatalogue.Instance;

    public bool CanLoad(ObpDestination destination) =>
        destination.Game == Game
        && string.Equals(destination.BuildId, BuildId, StringComparison.Ordinal)
        && ResolveNativeLevel(destination) is not null;

    public RuntimeWorld Load(string sourcePath, ObpDestination destination)
    {
        int level = ResolveNativeLevel(destination)
            ?? throw new ArgumentException($"Destination '{destination.DestinationId}' is not loadable by the Going Commando provider.", nameof(destination));

        using var reader = new FileRandomAccessReader(sourcePath);
        return GcWorldImport.Build(reader, level);
    }

    /// <summary>
    /// Resolve the file/index identity used by the GC importer. This intentionally
    /// does not use NativeEngineId: LEVEL21.WAD is file/index 21 even though the
    /// level self-reports engine id 30.
    /// </summary>
    public int? ResolveNativeLevel(ObpDestination destination)
    {
        if (destination.Game != Game
            || !string.Equals(destination.BuildId, BuildId, StringComparison.Ordinal)
            || !destination.NativeDestinationId.StartsWith("LEVEL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!int.TryParse(destination.NativeDestinationId.AsSpan(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out int level))
        {
            return null;
        }

        var canonical = GcDestinationCatalogue.Instance.FindByNativeLevel(level);
        return canonical is not null && string.Equals(canonical.DestinationId, destination.DestinationId, StringComparison.Ordinal)
            ? level
            : null;
    }
}
