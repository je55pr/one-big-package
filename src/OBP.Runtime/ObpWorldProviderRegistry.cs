using OBP.Core;

namespace OBP.Runtime;

/// <summary>
/// Validated composition of production world providers. It guarantees one
/// provider per source game, matching provider/catalogue provenance, and unique
/// global destination ids before the application starts routing requests.
/// </summary>
public sealed class ObpWorldProviderRegistry
{
    private readonly IReadOnlyList<IObpWorldProvider> _providers;
    private readonly Dictionary<ObpSourceGame, IObpWorldProvider> _byGame;
    private readonly Dictionary<string, ObpDestination> _byDestinationId;

    public ObpWorldProviderRegistry(IEnumerable<IObpWorldProvider> providers)
    {
        var array = providers.ToArray();
        _byGame = new Dictionary<ObpSourceGame, IObpWorldProvider>();
        _byDestinationId = new Dictionary<string, ObpDestination>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in array)
        {
            if (provider.Game != provider.Catalogue.Game)
            {
                throw new ArgumentException(
                    $"World provider {provider.GetType().Name} reports game {provider.Game}, but its catalogue reports {provider.Catalogue.Game}.",
                    nameof(providers));
            }
            if (!string.Equals(provider.BuildId, provider.Catalogue.BuildId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"World provider {provider.GetType().Name} reports build {provider.BuildId}, but its catalogue reports {provider.Catalogue.BuildId}.",
                    nameof(providers));
            }
            if (!_byGame.TryAdd(provider.Game, provider))
            {
                throw new ArgumentException($"More than one world provider is registered for {provider.Game}.", nameof(providers));
            }

            foreach (var destination in provider.Catalogue.Destinations)
            {
                if (destination.Game != provider.Game || !string.Equals(destination.BuildId, provider.BuildId, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Destination {destination.DestinationId} provenance does not match provider {provider.Game}/{provider.BuildId}.",
                        nameof(providers));
                }
                if (!_byDestinationId.TryAdd(destination.DestinationId, destination))
                {
                    throw new ArgumentException($"Duplicate global destination id '{destination.DestinationId}'.", nameof(providers));
                }
            }
        }

        _providers = Array.AsReadOnly(array);
        Games = new HashSet<ObpSourceGame>(_byGame.Keys);
    }

    public IReadOnlyList<IObpWorldProvider> Providers => _providers;
    public IReadOnlySet<ObpSourceGame> Games { get; }

    public IObpWorldProvider? Find(ObpSourceGame game) =>
        _byGame.TryGetValue(game, out var provider) ? provider : null;

    public ObpDestination? Resolve(string destinationId) =>
        _byDestinationId.TryGetValue(destinationId, out var destination) ? destination : null;

    public RuntimeWorld Load(string sourcePath, ObpDestination destination)
    {
        var provider = Find(destination.Game)
            ?? throw new InvalidOperationException($"No world provider is registered for {destination.Game}.");
        if (!provider.CanLoad(destination))
        {
            throw new InvalidOperationException(
                $"Provider {provider.Game}/{provider.BuildId} rejected destination {destination.DestinationId}.");
        }
        return provider.Load(sourcePath, destination);
    }
}
