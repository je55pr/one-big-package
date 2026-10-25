using System.Text.Json;
using System.Text.Json.Serialization;

namespace OBP.Core;

/// <summary>The single external source/world identity vocabulary; unknown input never becomes Synthetic.</summary>
public static class SourceIdentityWire
{
    public static ObpSourceGame ParseSource(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "rac1" or "r&c1" or "rc1" or "ratchet & clank" => ObpSourceGame.Rac1,
        "rac2" or "going commando" or "gc" or "r&c2" or "rc2" => ObpSourceGame.Rac2,
        "rac3" or "up your arsenal" or "uya" or "r&c3" or "rc3" => ObpSourceGame.Rac3,
        "deadlocked" => ObpSourceGame.Deadlocked,
        "synthetic" => ObpSourceGame.Synthetic,
        _ => throw new ArgumentException($"Unknown source game '{value}'. Expected rac1, rac2, rac3, deadlocked or synthetic."),
    };

    public static string FormatSource(ObpSourceGame game) => game switch
    {
        ObpSourceGame.Rac1 => "rac1",
        ObpSourceGame.Rac2 => "rac2",
        ObpSourceGame.Rac3 => "rac3",
        ObpSourceGame.Deadlocked => "deadlocked",
        ObpSourceGame.Synthetic => "synthetic",
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, "Unknown source game."),
    };

    public static WorldContentIdentity ParseWorld(string value) => value == "fusion" ? WorldContentIdentity.FusionLab :
        value is not null && value.StartsWith("obp:", StringComparison.Ordinal) ? WorldContentIdentity.Authored(value) :
        WorldContentIdentity.FromSource(ParseSource(value!));

    public static string FormatWorld(WorldContentIdentity identity) => identity.SourceGame is { } source ? FormatSource(source) :
        identity == WorldContentIdentity.FusionLab ? "fusion" : identity.AuthoredId ?? throw new ArgumentException("An uninitialised world identity has no wire value.");
}

/// <summary>
/// Opt in only on migrated string properties. Existing enum-backed archive,
/// avatar and replay contracts keep their established numeric JSON encoding.
/// </summary>
public sealed class SourceGameWireJsonConverter : JsonConverter<ObpSourceGame>
{
    public override ObpSourceGame Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("A source game must be a recognised string identifier.");
        try { return SourceIdentityWire.ParseSource(reader.GetString()!); }
        catch (ArgumentException error) { throw new JsonException(error.Message, error); }
    }
    public override void Write(Utf8JsonWriter writer, ObpSourceGame value, JsonSerializerOptions options) => writer.WriteStringValue(SourceIdentityWire.FormatSource(value));
}

public sealed class WorldContentIdentityJsonConverter : JsonConverter<WorldContentIdentity>
{
    public override WorldContentIdentity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("A world identity must be a source or authored-world string identifier.");
        try { return SourceIdentityWire.ParseWorld(reader.GetString()!); }
        catch (ArgumentException error) { throw new JsonException(error.Message, error); }
    }
    public override void Write(Utf8JsonWriter writer, WorldContentIdentity value, JsonSerializerOptions options) => writer.WriteStringValue(SourceIdentityWire.FormatWorld(value));
}
