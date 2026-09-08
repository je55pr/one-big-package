using OBP.Core;
using OBP.RAC1;
using OBP.RAC2;
using OBP.Runtime;

namespace OneBigPackage;

/// <summary>
/// Application composition root for production world providers. R&C1 and Going
/// Commando both hand neutral RuntimeWorld values to the same host; UYA can join
/// through the same provider contract without changing source/destination UI.
/// </summary>
public static class TrilogyWorldProviders
{
    public static readonly ObpWorldProviderRegistry Registry = new(
        [Rac1WorldProvider.Instance, GcWorldProvider.Instance]);

    public static IReadOnlyList<IObpWorldProvider> All => Registry.Providers;
    public static IReadOnlySet<ObpSourceGame> Games => Registry.Games;

    public static IObpWorldProvider? Find(ObpSourceGame game) => Registry.Find(game);
    public static ObpDestination? Resolve(string destinationId) => Registry.Resolve(destinationId);
}
