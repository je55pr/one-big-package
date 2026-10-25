using OBP.Core;
using System.Text.Json;
using System.Text.Json.Serialization;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;

namespace OBP.Fusion;

/// <summary>Opaque state belongs to its named adapter; unknown contributors survive profile round trips.</summary>
public sealed record ProfilePayload(string Owner, int Version, JsonElement Data)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extensions { get; init; }
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Owner) || Owner.Length > 160 || Version < 1 || Data.ValueKind != JsonValueKind.Object || Data.GetRawText().Length > 524288)
            throw new InvalidDataException("Invalid profile contributor.");
    }

    /// <summary>Retain opaque record fields; authored lists are replaced, never merged by array index.</summary>
    public ProfilePayload RetainUnknownFields(ProfilePayload? previous)
    {
        if (previous is null || previous.Owner != Owner || previous.Version != Version) return this;
        return this with { Data = MergeRecordFields(Data, previous.Data), Extensions = previous.Extensions };
    }

    internal static JsonElement MergeRecordFields(JsonElement current, JsonElement saved)
    {
        if (current.ValueKind != JsonValueKind.Object || saved.ValueKind != JsonValueKind.Object) return current;
        var fields = current.EnumerateObject().ToDictionary(field => field.Name, field => field.Value, StringComparer.Ordinal);
        foreach (var field in saved.EnumerateObject())
            fields[field.Name] = fields.TryGetValue(field.Name, out var present)
                ? field.Name is "Currencies" or "Preferences" ? present : MergeRecordFields(present, field.Value) : field.Value;
        return JsonSerializer.SerializeToElement(fields);
    }

    public static IReadOnlyList<string> UnknownRecordFields(JsonElement known, JsonElement saved, string prefix = "Player")
    {
        var result = new List<string>();
        if (known.ValueKind != JsonValueKind.Object || saved.ValueKind != JsonValueKind.Object) return result;
        foreach (var field in saved.EnumerateObject())
        {
            if (!known.TryGetProperty(field.Name, out var expected)) result.Add(prefix + "." + field.Name);
            else if (field.Name is not ("Preferences" or "Currencies")) result.AddRange(UnknownRecordFields(expected, field.Value, prefix + "." + field.Name));
        }
        return result;
    }
}

/// <summary>Payload-free source provenance. Never records machine paths or disc/asset bytes.</summary>
public sealed record ProfileSource([property: JsonConverter(typeof(SourceGameWireJsonConverter)), JsonRequired] ObpSourceGame Game, string Build, string Digest)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extensions { get; init; }
    public void Validate()
    {
        if (Game is not (ObpSourceGame.Rac1 or ObpSourceGame.Rac2 or ObpSourceGame.Rac3) || string.IsNullOrWhiteSpace(Build) || Build.Length > 128 ||
            Digest is null || Digest.Length != 64 || Digest.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("Invalid source dependency.");
    }
}

public sealed record ProfilePlayer(string PlayerId, long Generation, PlayerSessionConfiguration Configuration,
    PlayerLoadoutSnapshot Loadout, PlayerHealthSnapshot Health, IReadOnlyDictionary<string, long> Currencies,
    IReadOnlyDictionary<string, string> Preferences, IReadOnlyList<ProfilePayload> Components)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extensions { get; init; }

    public static ProfilePlayer Capture(PlayerSessionSnapshot state) => new(state.PlayerId, state.Generation, state.Configuration,
        state.Loadout, state.Health, state.Currencies, state.Preferences, state.Components.Select(component =>
            new ProfilePayload(component.ComponentId, component.Version, JsonSerializer.SerializeToElement(component, component.GetType(), ObpProfile.Options))).ToArray());

    public PlayerSessionSnapshot Restore(Func<ProfilePayload, IPlayerSessionComponentSnapshot> decode)
    {
        Validate();
        var snapshot = new PlayerSessionSnapshot(PlayerId, Generation, Configuration, Loadout, Health, Currencies)
        { Preferences = Preferences, Components = Components.Select(decode).ToArray() };
        snapshot.Validate();
        return snapshot;
    }

    public void Validate()
    {
        if (Configuration?.Avatar is null || Configuration.Movement is null || Configuration.Animation is null || Configuration.Services is null ||
            Loadout?.QuickSelect is null || Loadout.QuickSelect.Count > 8 || Health is null || Currencies is null || Preferences is null ||
            Currencies.Count > 64 || Preferences.Count > 256 || Preferences.Any(pair => pair.Key.Length > 160 || pair.Value is null || pair.Value.Length > 4096) ||
            Components is null || Components.Count > 64 || Components.Any(component => component is null) ||
            Components.Select(component => component.Owner).Distinct(StringComparer.Ordinal).Count() != Components.Count)
            throw new InvalidDataException("Invalid player contributors.");
        foreach (var component in Components) component.Validate();
        new PlayerSessionSnapshot(PlayerId, Generation, Configuration, Loadout, Health, Currencies) { Preferences = Preferences }.Validate();
    }
}

/// <summary>Ordinary OBP profile, independent of native campaign containers and scene transients.</summary>
public sealed record ObpProfile(int Version, string Integration, ProfilePlayer Player, string? Destination,
    IReadOnlyList<ProfileSource> Sources, IReadOnlyList<ProfilePayload> Campaigns)
{
    public const int CurrentVersion = 1;
    public const string CurrentIntegration = "obp/shared-player/v1";
    public const int MaximumBytes = 4 * 1024 * 1024;
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extensions { get; init; }
    public static JsonSerializerOptions Options { get; } = new() { MaxDepth = 64, WriteIndented = true };
    [JsonIgnore] public JsonElement? RetainedPlayerFields { get; init; }

    public static bool IsDestination(string? destination)
    {
        if (destination is null || destination.Length > 128) return false;
        var parts = destination.Split(':');
        return parts.Length == 2 && parts[0] is "rac1" or "rac2" or "rac3" or "obp" && parts[1].Length > 0 &&
            parts[1].All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }

    public void Validate()
    {
        if (Version != CurrentVersion) throw new UnsupportedProfileVersionException(Version);
        if (Integration != CurrentIntegration || Player is null || Destination is not null && !IsDestination(Destination) ||
            Sources is null || Sources.Count > 3 || Sources.Any(source => source is null) || Sources.Select(source => source.Game).Distinct().Count() != Sources.Count ||
            Campaigns is null || Campaigns.Count > 64 || Campaigns.Any(payload => payload is null) || Campaigns.Select(payload => payload.Owner).Distinct(StringComparer.Ordinal).Count() != Campaigns.Count)
            throw new InvalidDataException("Invalid shared profile envelope.");
        Player.Validate();
        foreach (var source in Sources) source.Validate();
        foreach (var campaign in Campaigns) campaign.Validate();
    }

    public void VerifyAvailableSources(IEnumerable<ProfileSource> attached)
    {
        var available = attached.ToDictionary(source => source.Game);
        foreach (var required in Sources)
            if (available.TryGetValue(required.Game, out var actual) && (required.Build != actual.Build ||
                !required.Digest.Equals(actual.Digest, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Profile requires another source build/digest for " + required.Game + "; original profile retained.");
    }

    public byte[] Encode()
    {
        Validate();
        var fields = JsonSerializer.SerializeToElement(this, Options).EnumerateObject().ToDictionary(field => field.Name, field => field.Value);
        if (RetainedPlayerFields is { } saved) fields["Player"] = ProfilePayload.MergeRecordFields(fields["Player"], saved);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(fields, Options);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Shared profile exceeds its size limit.");
        return bytes;
    }

    public static ObpProfile Decode(byte[] bytes)
    {
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Shared profile exceeds its size limit.");
        // Recognize future envelopes before decoding this version's required fields.
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("Version", out var version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number))
            throw new InvalidDataException("Shared profile lacks a schema version.");
        if (number != CurrentVersion) throw new UnsupportedProfileVersionException(number);
        var profile = JsonSerializer.Deserialize<ObpProfile>(bytes, Options) ?? throw new InvalidDataException("Shared profile is empty.");
        profile.Validate();
        return profile with { RetainedPlayerFields = document.RootElement.GetProperty("Player").Clone() };
    }
}

public sealed class UnsupportedProfileVersionException(int version) : Exception($"Shared profile version {version} is unsupported; original file retained.");
