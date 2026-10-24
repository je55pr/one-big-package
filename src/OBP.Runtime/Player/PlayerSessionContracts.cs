using OBP.Core;
using OBP.Runtime.Gameplay;

namespace OBP.Runtime.Player;

/// <summary>A selected native controller/profile, independent of world provenance.</summary>
public sealed record SourceProfileKey(ObpSourceGame SourceGame, string BuildId, string ProfileId)
{
    public void Validate()
    {
        if (!Enum.IsDefined(SourceGame) || string.IsNullOrWhiteSpace(BuildId) || string.IsNullOrWhiteSpace(ProfileId))
            throw new ArgumentException("A player profile requires a source, build and profile id.");
    }
}

/// <summary>Explicit OBP policy ids, resolved by registered adapters, never by planet origin.</summary>
public sealed record PlayerServiceBindings(
    string InputProfileId, string AttackRouteId, string HealthPolicyId,
    string CameraProfileId, string HudProfileId, string PauseMenuId,
    string QuickSelectId, string AudioProfileId);

/// <summary>
/// Desired player configuration. Selecting a profile does not prove its assets or
/// capabilities are available. Native controllers retain their own formulas.
/// </summary>
public sealed record PlayerSessionConfiguration(
    PlayerAvatarIdentity Avatar, SourceProfileKey Movement, SourceProfileKey Animation,
    PlayerServiceBindings Services, int NativeTicksPerSecond)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Avatar);
        Movement.Validate(); Animation.Validate();
        if (!Enum.IsDefined(Avatar.SourceGame) || string.IsNullOrWhiteSpace(Avatar.BuildId) ||
            string.IsNullOrWhiteSpace(Avatar.AvatarId) || string.IsNullOrWhiteSpace(Avatar.ModelId) ||
            NativeTicksPerSecond <= 0)
            throw new ArgumentException("A player needs an explicit avatar and positive native clock rate.");
        string[] ids = [Services.InputProfileId, Services.AttackRouteId, Services.HealthPolicyId,
            Services.CameraProfileId, Services.HudProfileId, Services.PauseMenuId,
            Services.QuickSelectId, Services.AudioProfileId];
        if (ids.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Every player service must have an explicit policy/adapter id.");
    }
}

/// <summary>
/// Selection facts, not a second inventory or catalog. Null hand equipment means
/// the wrench owns the hand; selecting a weapon need not draw it. #255 owns the
/// richer catalog and native equipment-state contributors to the player session.
/// </summary>
public sealed record PlayerLoadoutSnapshot(
    SourceWeaponKey? SelectedWeapon, SourceWeaponKey? HandEquipment,
    IReadOnlyList<SourceWeaponKey?> QuickSelect);

/// <summary>Resolved health facts. The selected health policy owns all consequences.</summary>
public sealed record PlayerHealthSnapshot(double Current, double Capacity, bool Alive);

/// <summary>
/// Versioned player-global state. Capture/restore implementations must copy
/// collections; no scene nodes, paths, retail bytes or world-local physics state.
/// This is not the durable save/replay format (#273/#274).
/// </summary>
public sealed record PlayerSessionSnapshot(
    string PlayerId, long Generation, PlayerSessionConfiguration Configuration,
    PlayerLoadoutSnapshot Loadout, PlayerHealthSnapshot Health,
    IReadOnlyDictionary<string, long> Currencies)
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public IReadOnlyList<IPlayerSessionComponentSnapshot> Components { get; init; } = [];
    public IReadOnlyDictionary<string, string> Preferences { get; init; } = new Dictionary<string, string>();

    public void Validate()
    {
        if (Version != CurrentVersion || string.IsNullOrWhiteSpace(PlayerId) || Generation < 0)
            throw new ArgumentException("Unsupported or invalid player session snapshot.");
        Configuration.Validate();
        Loadout.SelectedWeapon?.Validate(); Loadout.HandEquipment?.Validate();
        foreach (var weapon in Loadout.QuickSelect) weapon?.Validate();
        var components = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in Components)
            if (string.IsNullOrWhiteSpace(component.ComponentId) || component.Version < 1 || !components.Add(component.ComponentId))
                throw new ArgumentException("Invalid or duplicate source-owned player state component.");
        if (Preferences.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null))
            throw new ArgumentException("Invalid player preference.");
        if (!double.IsFinite(Health.Current) || !double.IsFinite(Health.Capacity) ||
            Health.Capacity <= 0 || Health.Current < 0 || Health.Current > Health.Capacity ||
            Currencies.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0))
            throw new ArgumentException("Invalid resolved player resources.");
    }
}

/// <summary>Immutable, source-owned state; Fusion never decodes its contents.</summary>
public interface IPlayerSessionComponentSnapshot
{
    string ComponentId { get; }
    int Version { get; }
}

public sealed record PlayerGameplaySnapshot(PlayerLoadoutSnapshot Loadout, PlayerHealthSnapshot Health,
    IReadOnlyDictionary<string, long> Currencies, IReadOnlyList<IPlayerSessionComponentSnapshot> Components);

/// <summary>
/// Persistent native controller owner supplied by the composition root. Native
/// campaign state is not this player state. World exit retires only transients.
/// </summary>
public interface IPlayerSessionStateAdapter
{
    PlayerGameplaySnapshot Capture();
    void ValidateRestore(PlayerGameplaySnapshot snapshot);
    void Restore(PlayerGameplaySnapshot snapshot);
    void EndWorld();
}

/// <summary>Host settings projection. Device calibration stays with the device, outside player state.</summary>
public interface IPlayerSessionPreferencesAdapter
{
    IReadOnlyDictionary<string, string> Capture();
    void ValidateRestore(IReadOnlyDictionary<string, string> preferences);
    void Restore(IReadOnlyDictionary<string, string> preferences);
}

public enum PlayerCapabilityStatus { Available, MissingSource, UnsupportedBuild, Unimplemented, Unowned, ContextBlocked }
public sealed record PlayerCapabilityAvailability(PlayerCapabilityStatus Status, string? Reason)
{
    public bool Usable => Status == PlayerCapabilityStatus.Available;
}

/// <summary>
/// One persistent owner, implemented by OBP.Fusion in #266. World leases never
/// own or dispose this session. Restore is explicit; entering a world is not a restore.
/// </summary>
public interface IObpPlayerSession
{
    PlayerSessionSnapshot Capture();
    void Restore(PlayerSessionSnapshot snapshot);
}
