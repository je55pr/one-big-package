using System.Text.Json;
using System.Text.Json.Serialization;

namespace OBP.Runtime.Presentation;

/// <summary>
/// How a <see cref="ShotSpec"/> frames the world. All three move the static
/// capture camera — the scripted-player capture keeps its own
/// <c>--test-scene player --capture-frame</c> path.
/// </summary>
public enum ShotCamera
{
    /// <summary>The per-planet establishing shot — a fixed camera over the trusted-geometry centre.</summary>
    Showcase,

    /// <summary>Straight down over the world centre.</summary>
    TopDown,

    /// <summary>An orbit position given by the shot's azimuth / elevation.</summary>
    Orbit,
}

/// <summary>
/// One named deterministic screenshot: a camera framing, an optional
/// <c>DebugOverlay</c> layer string (same syntax as <c>--overlay</c>), and how
/// many frames to settle before the grab. Engine-independent so a shot list can
/// be authored, validated and round-tripped without Godot.
/// </summary>
public sealed record ShotSpec(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("camera")] ShotCamera Camera = ShotCamera.Showcase,
    [property: JsonPropertyName("overlay")] string? Overlay = null,
    [property: JsonPropertyName("settleFrames")] int SettleFrames = 120,
    [property: JsonPropertyName("azimuthDegrees")] double AzimuthDegrees = 38,
    [property: JsonPropertyName("elevationDegrees")] double ElevationDegrees = 26);

/// <summary>An ordered set of <see cref="ShotSpec"/>s, optionally pinned to one world token.</summary>
public sealed record ShotList(
    [property: JsonPropertyName("world")] string? World,
    [property: JsonPropertyName("shots")] IReadOnlyList<ShotSpec> Shots)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ShotList Parse(string json)
    {
        var list = JsonSerializer.Deserialize<ShotList>(json, Options)
            ?? throw new JsonException("shot list is empty");
        if (list.Shots is null || list.Shots.Count == 0)
        {
            throw new JsonException("shot list has no shots");
        }

        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var s in list.Shots)
        {
            if (string.IsNullOrWhiteSpace(s.Name))
            {
                throw new JsonException("every shot needs a name");
            }

            if (!seen.Add(s.Name))
            {
                throw new JsonException($"duplicate shot name '{s.Name}'");
            }

            if (s.SettleFrames < 1)
            {
                throw new JsonException($"shot '{s.Name}' has settleFrames < 1");
            }
        }

        return list;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);
}
