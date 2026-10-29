using OBP.Core;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>Menu visibility is based on actual provider loadability, not only the destination's classification.</summary>
internal static class DestinationSelectorVisibility
{
    public static bool IsVisible(ObpDestination destination, IObpWorldProvider provider, bool debugOutputEnabled) =>
        debugOutputEnabled || destination.Kind != ObpDestinationKind.Unresolved || provider.CanLoad(destination);
}
