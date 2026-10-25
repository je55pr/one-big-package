using System.Text.Json;
using Godot;
using OBP.Fusion;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Progression;
using OBP.Runtime.Player;

namespace OneBigPackage;

public partial class OBPGame
{
    private ObpProfile? _profileDocument;
    private bool _profileInitialized, _profileWriteBlocked;
    private string? _profileStatus;
    private double _profileSaveElapsed;
    private byte[]? _profileLastSaved;
    private readonly HashSet<string> _opaqueCampaignOwners = [];
    private int _profileRetainedFieldCount;
    private string? _lastCommittedProfileDestination;
    private string? SharedProfilePath => _args.NoProfile || !OrdinaryPlayerSession ? null :
        _args.ProfilePath is { } path ? Path.GetFullPath(path) : DisplayServer.GetName() == "headless" || AutomationRunPolicy.IsAutomated(OS.GetCmdlineUserArgs(), OS.GetCmdlineArgs()) ? null : ProjectSettings.GlobalizePath("user://obp-profile.json");

    private static IPlayerSessionComponentSnapshot DecodeProfileComponent(ProfilePayload payload)
    {
        if (payload.Owner != "obp/native-player/v2" || payload.Version != 2 ||
            payload.Data.TryGetProperty("Version", out var version) && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number) || number != 2) ||
            payload.Data.TryGetProperty("ComponentId", out var owner) && (owner.ValueKind != JsonValueKind.String || owner.GetString() != payload.Owner))
            throw new InvalidDataException("Player contributor is not installed: " + payload.Owner);
        return payload.Data.Deserialize<PlayerSessionNativeState.State>(ObpProfile.Options) ?? throw new InvalidDataException("Player contributor is empty.");
    }

    private IReadOnlyList<ProfileSource> CaptureProfileSources() => (_profileDocument?.Sources ?? [])
        .Where(saved => !_sources.Attached.Values.Any(source => source.Game.ToString().Equals(saved.Game, StringComparison.OrdinalIgnoreCase)))
        .Concat(_sources.Attached.Values.Select(source => new ProfileSource(source.Game.ToString().ToLowerInvariant(),
            source.Identity.BuildId, source.Identity.Sha256 ?? throw new InvalidDataException("Source has no authority digest."))
        { Extensions = _profileDocument?.Sources.SingleOrDefault(saved => saved.Game.Equals(source.Game.ToString(), StringComparison.OrdinalIgnoreCase))?.Extensions }))
        .OrderBy(source => source.Game, StringComparer.Ordinal).ToArray();

    private ObpProfile CaptureSharedProfile()
    {
        var campaigns = (_profileDocument?.Campaigns ?? []).Where(payload => payload.Owner != "rac1/campaign" || _opaqueCampaignOwners.Contains(payload.Owner)).ToList();
        if (!_opaqueCampaignOwners.Contains("rac1/campaign"))
            campaigns.Add(new ProfilePayload("rac1/campaign", 1, JsonSerializer.SerializeToElement(Rac1CampaignSavePolicy.Capture(
                _rac1CampaignSession.Campaign, _rac1CampaignSession.Weapons, _parityBoltWallet,
                _rac1CollectedNovalisGoldBolts, _rac1CampaignSession.BuriedBolts))).RetainUnknownFields(_profileDocument?.Campaigns.SingleOrDefault(payload => payload.Owner == "rac1/campaign")));
        var player = ProfilePlayer.Capture(_playerSession!.Capture());
        if (_profileDocument?.Player is { } previous) player = player with
        {
            Extensions = previous.Extensions,
            Components = player.Components.Select(payload => payload.RetainUnknownFields(previous.Components.SingleOrDefault(saved => saved.Owner == payload.Owner))).ToArray(),
        };
        return new(ObpProfile.CurrentVersion, ObpProfile.CurrentIntegration, player,
            _lastCommittedProfileDestination ?? _profileDocument?.Destination, CaptureProfileSources(), campaigns)
        { Extensions = _profileDocument?.Extensions, RetainedPlayerFields = _profileDocument?.RetainedPlayerFields };
    }

    private void InitializeSharedProfile()
    {
        if (_profileInitialized || _playerSession is null || !OrdinaryPlayerSession) return;
        _profileInitialized = true;
        if (SharedProfilePath is not { } path) return;
        try
        {
            var loaded = AtomicDocumentFile.Load(path, ObpProfile.MaximumBytes, ObpProfile.Decode,
                error => error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException);
            _profileStatus = loaded.Problem;
            if (loaded.Kind == DocumentReadKind.Missing && File.Exists(ProjectSettings.GlobalizePath("user://rac1-campaign.json")))
                _profileStatus = "Native R&C1 campaign kept separate. Ordinary play uses the shared profile; --rac1-campaign-save opens the original native parity mode.";
            _profileWriteBlocked = loaded.Kind == DocumentReadKind.Rejected;
            if (loaded.Value is { } profile)
            {
                ApplySharedProfile(profile);
                _profileLastSaved = loaded.Kind == DocumentReadKind.Primary ? profile.Encode() : null;
                GD.Print($"[profile] restored shared player ({loaded.Kind}); native campaign file remains isolated");
            }
            else if (_profileWriteBlocked) GD.Print("[profile] " + _profileStatus);
        }
        catch (Exception error) when (error is UnsupportedProfileVersionException or InvalidDataException or ArgumentException or JsonException)
        {
            _profileWriteBlocked = true;
            _profileStatus = "Profile retained without replacement: " + error.Message;
            GD.Print("[profile] " + _profileStatus);
        }
    }

    private void ApplySharedProfile(ObpProfile profile)
    {
        profile.Validate();
        profile.VerifyAvailableSources(_sources.Attached.Values.Select(source => new ProfileSource(source.Game.ToString().ToLowerInvariant(),
            source.Identity.BuildId, source.Identity.Sha256 ?? "")));
        var snapshot = profile.Player.Restore(DecodeProfileComponent);
        _playerNative!.ValidateRestore(new(snapshot.Loadout, snapshot.Health, snapshot.Currencies, snapshot.Components));
        Rac1CampaignRestoreResult? campaign = null;
        var opaque = new HashSet<string>(StringComparer.Ordinal);
        foreach (var payload in profile.Campaigns)
        {
            if (payload.Owner == "rac1/campaign" && payload.Version == 1 &&
                payload.Data.TryGetProperty("SchemaVersion", out var schema) && schema.ValueKind == JsonValueKind.Number && schema.TryGetInt32(out int number) && number is 1 or Rac1CampaignSavePolicy.CurrentSchemaVersion)
                campaign = Rac1CampaignSavePolicy.RestoreOrDefault(payload.Data.Deserialize<Rac1CampaignSaveEnvelope>() ?? throw new InvalidDataException("Campaign contributor is empty."));
            else opaque.Add(payload.Owner);
        }
        _playerSession!.Restore(snapshot);
        if (campaign is not null)
        {
            _rac1CampaignSession = new(campaign.Campaign, campaign.Weapons, Rac1BuriedBoltProgress.Restore(campaign.BuriedBoltHistory));
            _parityBoltWallet = new(campaign.Bolts);
            _rac1CollectedNovalisGoldBolts.Clear(); _rac1CollectedNovalisGoldBolts.UnionWith(campaign.NovalisGoldBoltInstances);
        }
        _opaqueCampaignOwners.Clear(); _opaqueCampaignOwners.UnionWith(opaque);
        _profileDocument = profile;
        RefreshPlayerSourceAvailability();
        _profileRetainedFieldCount = (profile.Extensions?.Count ?? 0) + (profile.Player.Extensions?.Count ?? 0);
        _profileRetainedFieldCount += profile.Sources.Sum(source => source.Extensions?.Count ?? 0);
        if (profile.RetainedPlayerFields is { } original)
            _profileRetainedFieldCount += ProfilePayload.UnknownRecordFields(JsonSerializer.SerializeToElement(ProfilePlayer.Capture(_playerSession.Capture()), ObpProfile.Options), original).Count;
        foreach (var payload in profile.Player.Components)
            _profileRetainedFieldCount += ProfilePayload.UnknownRecordFields(JsonSerializer.SerializeToElement(DecodeProfileComponent(payload), typeof(PlayerSessionNativeState.State), ObpProfile.Options), payload.Data).Count;
        if (opaque.Count > 0) _profileStatus = "Retained source progress without installed adapters: " + string.Join(", ", opaque);
        if (_profileRetainedFieldCount > 0) _profileStatus = $"Retained {_profileRetainedFieldCount} unknown profile fields. " + _profileStatus;
    }

    private bool SaveSharedProfile(string reason)
    {
        if (SharedProfilePath is not { } path || _playerSession is null || _profileWriteBlocked || _playtest is not null) return false;
        try
        {
            var profile = CaptureSharedProfile(); byte[] bytes = profile.Encode();
            if (_profileLastSaved is { } saved && saved.AsSpan().SequenceEqual(bytes)) return true;
            AtomicDocumentFile.Save(path, bytes, ObpProfile.MaximumBytes, existing =>
            {
                try { _ = ObpProfile.Decode(existing); return true; }
                catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException) { return false; }
            });
            _profileDocument = profile; _profileLastSaved = bytes;
            _profileStatus = _opaqueCampaignOwners.Count == 0 ? "Shared profile saved." : "Shared profile saved; unavailable campaign contributors retained: " + string.Join(", ", _opaqueCampaignOwners);
            if (_profileRetainedFieldCount > 0) _profileStatus += $" Retained {_profileRetainedFieldCount} unknown fields.";
            GD.Print("[profile] saved " + reason);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException or UnsupportedProfileVersionException)
        { _profileStatus = "Profile was not saved: " + error.Message; GD.Print("[profile] " + _profileStatus); return false; }
    }

    private void TickSharedProfile(double delta)
    {
        if (SharedProfilePath is null || _mode != Mode.World) return;
        _profileSaveElapsed += delta;
        if (_profileSaveElapsed < 10) return;
        _profileSaveElapsed = 0; SaveSharedProfile("autosave");
    }

    public bool TryResumeSharedProfileFromBootstrap()
    {
        EnsurePlayerSession();
        if (_profileWriteBlocked || _profileDocument?.Destination is not { } destination) return false;
        if (destination == "obp:fusion-lab") return EnterFusionLab();
        return TrilogyWorldProviders.Resolve(destination) is { } native && OnDestinationChosen(native);
    }
}
