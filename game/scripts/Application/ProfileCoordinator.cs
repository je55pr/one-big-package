using System.Text.Json;
using OBP.Fusion;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Progression;
using OBP.Runtime.Player;

namespace OBP.Game;

internal sealed class ProfileCoordinator(ProfileCoordinatorPorts ports)
{
    private readonly ProfileCoordinatorPorts _ports = ports;
    internal ObpProfile? Document => _profileDocument;
    internal string? Status => _profileStatus;
    internal bool WriteBlocked => _profileWriteBlocked;
    internal string? LastCommittedDestination { get => _lastCommittedProfileDestination; set => _lastCommittedProfileDestination = value; }
    internal bool RecoveryRequired => _profileWriteBlocked;
    private ObpProfile? _profileDocument;
    private bool _profileInitialized, _profileWriteBlocked, _profileSaveFailed;
    private string? _profileStatus;
    internal string? SaveNotice => _profileWriteBlocked
        ? "Your saved game could not be opened. Continue without saving; your existing progress will be kept."
        : _profileSaveFailed ? "Progress could not be saved. Your previous save has been kept." : null;
    private double _profileSaveElapsed;
    private byte[]? _profileLastSaved;
    private readonly HashSet<string> _opaqueCampaignOwners = [];
    private int _profileRetainedFieldCount;
    private string? _lastCommittedProfileDestination;
    internal string? SharedProfilePath => _ports.ProfilePath();

    private static IPlayerSessionComponentSnapshot DecodeProfileComponent(ProfilePayload payload)
    {
        if (payload.Owner != "obp/native-player/v2" || payload.Version != 2 ||
            payload.Data.TryGetProperty("Version", out var version) && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number) || number != 2) ||
            payload.Data.TryGetProperty("ComponentId", out var owner) && (owner.ValueKind != JsonValueKind.String || owner.GetString() != payload.Owner))
            throw new InvalidDataException("Player contributor is not installed: " + payload.Owner);
        return payload.Data.Deserialize<PlayerSessionNativeState.State>(ObpProfile.Options) ?? throw new InvalidDataException("Player contributor is empty.");
    }

    internal IReadOnlyList<ProfileSource> CaptureProfileSources()
    {
        var attached = _ports.AttachedSources();
        return (_profileDocument?.Sources ?? [])
            .Where(saved => !attached.Any(source => source.Game == saved.Game))
            .Concat(attached.Select(source => source with
            { Extensions = _profileDocument?.Sources.SingleOrDefault(saved => saved.Game == source.Game)?.Extensions }))
            .OrderBy(source => source.Game).ToArray();
    }

    internal ObpProfile Capture()
    {
        var campaigns = (_profileDocument?.Campaigns ?? []).Where(payload => payload.Owner != "rac1/campaign" || _opaqueCampaignOwners.Contains(payload.Owner)).ToList();
        if (!_opaqueCampaignOwners.Contains("rac1/campaign"))
            campaigns.Add(_ports.CaptureCampaign(_profileDocument?.Campaigns.SingleOrDefault(payload => payload.Owner == "rac1/campaign")));

        var player = ProfilePlayer.Capture(_ports.Player()!.Capture());
        if (_profileDocument?.Player is { } previous) player = player with
        {
            Extensions = previous.Extensions,
            Components = player.Components.Select(payload => payload.RetainUnknownFields(previous.Components.SingleOrDefault(saved => saved.Owner == payload.Owner))).ToArray(),
        };
        return new(ObpProfile.CurrentVersion, ObpProfile.CurrentIntegration, player,
            _lastCommittedProfileDestination ?? _profileDocument?.Destination, CaptureProfileSources(), campaigns)
        { Extensions = _profileDocument?.Extensions, RetainedPlayerFields = _profileDocument?.RetainedPlayerFields };
    }

    internal void Initialize()
    {
        if (_profileInitialized || _ports.Player() is null || !_ports.OrdinaryPlayer()) return;
        _profileInitialized = true;
        if (SharedProfilePath is not { } path) return;
        try
        {
            var loaded = AtomicDocumentFile.Load(path, ObpProfile.MaximumBytes, ObpProfile.Decode,
                error => error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException);
            _profileStatus = loaded.Problem;
            if (loaded.Kind == DocumentReadKind.Missing && _ports.LegacyCampaignExists())
                _profileStatus = "Native R&C1 campaign kept separate. Ordinary play uses the shared profile; --rac1-campaign-save opens the original native parity mode.";
            _profileWriteBlocked = loaded.Kind == DocumentReadKind.Rejected;
            if (loaded.Value is { } profile)
            {
                Apply(profile);
                _profileLastSaved = loaded.Kind == DocumentReadKind.Primary ? profile.Encode() : null;
                _ports.Log($"[profile] restored shared player ({loaded.Kind}); native campaign file remains isolated");
            }
            else if (_profileWriteBlocked) _ports.Log("[profile] " + _profileStatus);
        }
        catch (Exception error) when (error is UnsupportedProfileVersionException or InvalidDataException or ArgumentException or JsonException)
        {
            _profileWriteBlocked = true;
            _profileStatus = "Profile retained without replacement: " + error.Message;
            _ports.Log("[profile] " + _profileStatus);
        }
    }

    internal void Apply(ObpProfile profile)
    {
        profile.Validate();
        profile.VerifyAvailableSources(_ports.AttachedSources());
        var snapshot = profile.Player.Restore(DecodeProfileComponent);
        _ports.Native()!.ValidateRestore(new(snapshot.Loadout, snapshot.Health, snapshot.Currencies, snapshot.Components));
        Rac1CampaignRestoreResult? campaign = null;
        var opaque = new HashSet<string>(StringComparer.Ordinal);
        foreach (var payload in profile.Campaigns)
        {
            if (payload.Owner == "rac1/campaign" && payload.Version == 1 &&
                payload.Data.TryGetProperty("SchemaVersion", out var schema) && schema.ValueKind == JsonValueKind.Number && schema.TryGetInt32(out int number) && number is 1 or Rac1CampaignSavePolicy.CurrentSchemaVersion)
                campaign = Rac1CampaignSavePolicy.RestoreOrDefault(payload.Data.Deserialize<Rac1CampaignSaveEnvelope>() ?? throw new InvalidDataException("Campaign contributor is empty."));
            else opaque.Add(payload.Owner);
        }
        _ports.Player()!.Restore(snapshot);
        if (campaign is not null)
        {
            _ports.RestoreCampaign(campaign);
        }
        _opaqueCampaignOwners.Clear(); _opaqueCampaignOwners.UnionWith(opaque);
        _profileDocument = profile;
        _ports.RefreshPlayerSources();
        _profileRetainedFieldCount = (profile.Extensions?.Count ?? 0) + (profile.Player.Extensions?.Count ?? 0);
        _profileRetainedFieldCount += profile.Sources.Sum(source => source.Extensions?.Count ?? 0);
        if (profile.RetainedPlayerFields is { } original)
            _profileRetainedFieldCount += ProfilePayload.UnknownRecordFields(JsonSerializer.SerializeToElement(ProfilePlayer.Capture(_ports.Player()!.Capture()), ObpProfile.Options), original).Count;
        foreach (var payload in profile.Player.Components)
            _profileRetainedFieldCount += ProfilePayload.UnknownRecordFields(JsonSerializer.SerializeToElement(DecodeProfileComponent(payload), typeof(PlayerSessionNativeState.State), ObpProfile.Options), payload.Data).Count;
        if (opaque.Count > 0) _profileStatus = "Retained source progress without installed adapters: " + string.Join(", ", opaque);
        if (_profileRetainedFieldCount > 0) _profileStatus = $"Retained {_profileRetainedFieldCount} unknown profile fields. " + _profileStatus;
    }

    internal bool Save(string reason)
    {
        if (SharedProfilePath is not { } path || _ports.Player() is null || _profileWriteBlocked || _ports.WritesSuspended() || _ports.PlaytestActive()) return false;
        try
        {
            var profile = Capture(); byte[] bytes = profile.Encode();
            if (_profileLastSaved is { } saved && saved.AsSpan().SequenceEqual(bytes)) { _profileSaveFailed = false; return true; }
            AtomicDocumentFile.Save(path, bytes, ObpProfile.MaximumBytes, existing =>
            {
                try { _ = ObpProfile.Decode(existing); return true; }
                catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException) { return false; }
            });
            _profileDocument = profile; _profileLastSaved = bytes; _profileSaveFailed = false;
            _profileStatus = _opaqueCampaignOwners.Count == 0 ? "Shared profile saved." : "Shared profile saved; unavailable campaign contributors retained: " + string.Join(", ", _opaqueCampaignOwners);
            if (_profileRetainedFieldCount > 0) _profileStatus += $" Retained {_profileRetainedFieldCount} unknown fields.";
            _ports.Log("[profile] saved " + reason);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException or UnsupportedProfileVersionException)
        { _profileSaveFailed = true; _profileStatus = "Profile was not saved: " + error.Message; _ports.Log("[profile] " + _profileStatus); return false; }
    }

    internal void Tick(double delta)
    {
        if (!_ports.WorldActive() || SharedProfilePath is null) return;
        _profileSaveElapsed += delta;
        if (_profileSaveElapsed < 10) return;
        _profileSaveElapsed = 0; Save("autosave");
    }

}

/// <summary>Explicit host boundaries. Native campaign policies remain in source owners.</summary>
internal sealed record ProfileCoordinatorPorts(
    Func<string?> ProfilePath,
    Func<bool> OrdinaryPlayer,
    Func<ObpPlayerSession?> Player,
    Func<PlayerSessionNativeState?> Native,
    Func<IReadOnlyList<ProfileSource>> AttachedSources,
    Func<bool> LegacyCampaignExists,
    Func<ProfilePayload?, ProfilePayload> CaptureCampaign,
    Action<Rac1CampaignRestoreResult> RestoreCampaign,
    Action RefreshPlayerSources,
    Func<bool> WritesSuspended,
    Func<bool> PlaytestActive,
    Func<bool> WorldActive,
    Action<string> Log);
