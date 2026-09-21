using OBP.Core;

namespace OBP.Runtime.Player;

/// <summary>
/// Source-keyed composition registry for playable-avatar providers. Missing
/// providers are an explicit unsupported presentation state, never permission to
/// borrow another game's avatar or animation selector.
/// </summary>
public sealed class PlayerAvatarProviderRegistry
{
    private readonly IReadOnlyDictionary<ObpSourceGame, IPlayerAvatarProvider> _providers;

    public PlayerAvatarProviderRegistry(IEnumerable<IPlayerAvatarProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var map = new Dictionary<ObpSourceGame, IPlayerAvatarProvider>();
        foreach (IPlayerAvatarProvider provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (!map.TryAdd(provider.SourceGame, provider))
                throw new ArgumentException(
                    $"Duplicate player-avatar provider for {provider.SourceGame}.",
                    nameof(providers));
        }
        _providers = map;
    }

    public IPlayerAvatarProvider? Get(ObpSourceGame game) =>
        _providers.TryGetValue(game, out IPlayerAvatarProvider? provider)
            ? provider
            : null;

    public bool HasProvider(ObpSourceGame game) => _providers.ContainsKey(game);
}
