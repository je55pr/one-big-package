using OBP.Core;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>Menu visibility is based on actual provider loadability, not only the destination's classification.</summary>
internal static class DestinationSelectorVisibility
{
    public static bool IsVisible(ObpDestination destination, IObpWorldProvider provider, bool debugOutputEnabled) =>
        debugOutputEnabled || destination.Kind != ObpDestinationKind.Unresolved || provider.CanLoad(destination);

    public static bool HasHiddenDestinations(IEnumerable<ObpDestination> destinations, IObpWorldProvider provider) =>
        destinations.Any(destination => !IsVisible(destination, provider, debugOutputEnabled: false));

    public static string DisambiguatedName(ObpDestination destination, IReadOnlyList<ObpDestination> catalogue) =>
        catalogue.Count(other => string.Equals(other.DisplayName, destination.DisplayName, StringComparison.OrdinalIgnoreCase)) > 1
            ? $"{destination.DisplayName} ({destination.NativeDestinationId})"
            : destination.DisplayName;
}
