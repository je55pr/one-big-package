using OBP.Core;
using OBP.RAC1;
using OBP.RAC2;
using OBP.RAC3;
using OBP.Runtime;

namespace OneBigPackage;

/// <summary>
/// Application composition root for production trilogy world providers. Every
/// source game hands a neutral RuntimeWorld to the same host.
/// </summary>
public static class TrilogyWorldProviders
{
    public static readonly ObpWorldProviderRegistry Registry = new(
        [Rac1WorldProvider.Instance, GcWorldProvider.Instance, Rac3WorldProvider.Instance]);

    public static IReadOnlyList<IObpWorldProvider> All => Registry.Providers;
    public static IReadOnlySet<ObpSourceGame> Games => Registry.Games;

    public static IObpWorldProvider? Find(ObpSourceGame game) => Registry.Find(game);
    public static ObpDestination? Resolve(string destinationId) => Registry.Resolve(destinationId);
}
