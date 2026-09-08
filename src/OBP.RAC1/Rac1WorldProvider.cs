using System.Globalization;
using OBP.Core;
using OBP.IO;
using OBP.Runtime;

namespace OBP.RAC1;

/// <summary>
/// R&C1 implementation of the neutral runtime-world provider. Native destination
/// identity stays in <see cref="Rac1DestinationCatalogue"/> and retail decoding
/// stays in <see cref="Rac1WorldImport"/>.
/// </summary>
public sealed class Rac1WorldProvider : IObpWorldProvider
{
    public static readonly Rac1WorldProvider Instance = new();

    private Rac1WorldProvider() { }

    public ObpSourceGame Game => ObpSourceGame.Rac1;
    public string BuildId => Rac1Authority.Primary.BuildId;
    public IObpDestinationCatalogue Catalogue => Rac1DestinationCatalogue.Instance;

    public bool CanLoad(ObpDestination destination) =>
        destination.Game == Game
        && string.Equals(destination.BuildId, BuildId, StringComparison.Ordinal)
        && ResolveNativeLevel(destination) is not null;

    public RuntimeWorld Load(string sourcePath, ObpDestination destination)
    {
        int level = ResolveNativeLevel(destination)
            ?? throw new ArgumentException($"Destination '{destination.DestinationId}' is not loadable by the R&C1 provider.", nameof(destination));

        using var reader = new FileRandomAccessReader(sourcePath);
        return Rac1WorldImport.Build(reader, level);
    }

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

        var canonical = Rac1DestinationCatalogue.Instance.FindByNativeLevel(level);
        return canonical is not null
            && string.Equals(canonical.DestinationId, destination.DestinationId, StringComparison.Ordinal)
            ? level
            : null;
    }
}
