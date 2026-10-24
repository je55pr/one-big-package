using System.Collections.ObjectModel;
using OBP.Core;
using OBP.Runtime.Player;

namespace OBP.Fusion;

/// <summary>One application-owned player, independent of any world lease.</summary>
public sealed class ObpPlayerSession : IObpPlayerSession
{
    private PlayerSessionSnapshot _state;
    private readonly IPlayerSessionStateAdapter? _native;
    private readonly IPlayerSessionPreferencesAdapter? _preferences;
    private readonly Dictionary<ObpSourceGame, string> _sources = [];

    public ObpPlayerSession(PlayerSessionSnapshot initial, IPlayerSessionStateAdapter? native = null,
        IPlayerSessionPreferencesAdapter? preferences = null)
    {
        initial.Validate();
        _state = Freeze(initial);
        _native = native;
        _preferences = preferences;
    }

    public PlayerSessionSnapshot Capture()
    {
        var state = _state;
        if (_native is { } native)
        {
            var gameplay = native.Capture();
            state = state with
            {
                Loadout = gameplay.Loadout,
                Health = gameplay.Health,
                Currencies = gameplay.Currencies,
                Components = gameplay.Components
            };
        }
        if (_preferences is { } preferences)
        {
            var current = new Dictionary<string, string>(state.Preferences, StringComparer.Ordinal);
            foreach (var pair in preferences.Capture()) current[pair.Key] = pair.Value;
            state = state with { Preferences = current };
        }
        state.Validate();
        return Freeze(state);
    }

    public void Configure(PlayerSessionConfiguration configuration)
    {
        configuration.Validate();
        _state = _state with { Configuration = configuration, Generation = checked(_state.Generation + 1) };
    }

    public void SetPreference(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        var preferences = new Dictionary<string, string>(Capture().Preferences, StringComparer.Ordinal) { [key] = value };
        _preferences?.ValidateRestore(preferences);
        _preferences?.Restore(preferences);
        _state = _state with { Preferences = new ReadOnlyDictionary<string, string>(preferences) };
    }

    /// <summary>Explicit restore invalidates old observers; world entry never calls this.</summary>
    public void Restore(PlayerSessionSnapshot snapshot)
    {
        snapshot.Validate();
        if (snapshot.PlayerId != _state.PlayerId) throw new ArgumentException("A snapshot belongs to a different player.");
        var frozen = Freeze(snapshot);
        long generation = checked(Math.Max(_state.Generation, snapshot.Generation) + 1);
        var gameplay = new PlayerGameplaySnapshot(frozen.Loadout, frozen.Health, frozen.Currencies, frozen.Components);
        _native?.ValidateRestore(gameplay);
        _preferences?.ValidateRestore(frozen.Preferences);
        var before = _native?.Capture();
        var previousPreferences = _preferences?.Capture();
        try
        {
            _native?.Restore(gameplay);
            _preferences?.Restore(frozen.Preferences);
        }
        catch
        {
            if (before is not null) _native!.Restore(before);
            if (previousPreferences is not null) _preferences!.Restore(previousPreferences);
            throw;
        }
        _state = frozen with { Generation = generation };
    }

    public void EndWorld() => _native?.EndWorld();

    public void SetAttachedSources(IEnumerable<ObpBuildIdentity> sources)
    {
        var current = sources.ToDictionary(source => source.Game, source => source.BuildId);
        _sources.Clear();
        foreach (var source in current) _sources.Add(source.Key, source.Value);
    }

    public PlayerCapabilityAvailability Availability(ObpSourceGame source, string build, bool implemented, bool owned, bool contextAllowed = true)
    {
        if (!implemented) return new(PlayerCapabilityStatus.Unimplemented, "This capability has not been implemented.");
        if (!_sources.TryGetValue(source, out var attached)) return new(PlayerCapabilityStatus.MissingSource, $"Attach the selected {source} source.");
        if (attached != build) return new(PlayerCapabilityStatus.UnsupportedBuild, $"The selected capability requires {build}.");
        if (!owned) return new(PlayerCapabilityStatus.Unowned, "The selected loadout does not own this capability.");
        if (!contextAllowed) return new(PlayerCapabilityStatus.ContextBlocked, "The current contact/action context does not admit this capability.");
        return new(PlayerCapabilityStatus.Available, null);
    }

    private static PlayerSessionSnapshot Freeze(PlayerSessionSnapshot state) => state with
    {
        Loadout = state.Loadout with { QuickSelect = Array.AsReadOnly(state.Loadout.QuickSelect.ToArray()) },
        Currencies = new ReadOnlyDictionary<string, long>(new Dictionary<string, long>(state.Currencies, StringComparer.Ordinal)),
        Components = Array.AsReadOnly(state.Components.ToArray()),
        Preferences = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(state.Preferences, StringComparer.Ordinal)),
    };
}
