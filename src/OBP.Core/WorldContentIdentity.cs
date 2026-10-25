using System.Text.Json.Serialization;

namespace OBP.Core;

/// <summary>A native source or a deliberately authored OBP world; neither implies player ownership.</summary>
[JsonConverter(typeof(WorldContentIdentityJsonConverter))]
public readonly record struct WorldContentIdentity
{
    public ObpSourceGame? SourceGame { get; }
    public string? AuthoredId { get; }
    public bool IsAuthored => AuthoredId is not null;

    private WorldContentIdentity(ObpSourceGame? sourceGame, string? authoredId) => (SourceGame, AuthoredId) = (sourceGame, authoredId);

    public static WorldContentIdentity FromSource(ObpSourceGame game)
    {
        _ = SourceIdentityWire.FormatSource(game);
        return new(game, null);
    }

    public static WorldContentIdentity Authored(string id)
    {
        if (id is null || !id.StartsWith("obp:", StringComparison.Ordinal) || id.Length <= 4 || id.Any(char.IsWhiteSpace))
            throw new ArgumentException("An authored world needs an explicit obp:<id> identity.", nameof(id));
        return new(null, id);
    }

    public static WorldContentIdentity FusionLab => Authored("obp:fusion-lab");
    public static implicit operator WorldContentIdentity(ObpSourceGame game) => FromSource(game);
    public override string ToString() => SourceIdentityWire.FormatWorld(this);
}
