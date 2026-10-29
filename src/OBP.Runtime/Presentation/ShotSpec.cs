using System.Text.Json;
using System.Text.Json.Serialization;

namespace OBP.Runtime.Presentation;

/// <summary>
/// How a <see cref="ShotSpec"/> frames the world. These cameras never replace
/// an ordinary player's camera.
/// </summary>
public enum ShotCamera
{
    Showcase,
    TopDown,
    Orbit,
}

/// <summary>
/// One named deterministic screenshot. A focused orbit is an explicitly
/// authored OBP capture tool, not a claim about source-game camera behaviour.
/// Focus uses neutral Y-up world coordinates, not screen or native Z-up units.
/// </summary>
public sealed record ShotSpec(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("camera")] ShotCamera Camera = ShotCamera.Showcase,
    [property: JsonPropertyName("overlay")] string? Overlay = null,
    [property: JsonPropertyName("settleFrames")] int SettleFrames = 120,
    [property: JsonPropertyName("azimuthDegrees")] double AzimuthDegrees = 38,
    [property: JsonPropertyName("elevationDegrees")] double ElevationDegrees = 26,
    [property: JsonPropertyName("focus")] double[]? Focus = null,
    [property: JsonPropertyName("distance")] double? Distance = null,
    [property: JsonPropertyName("hideHud")] bool HideHud = false);

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
            throw new JsonException("shot list has no shots");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var shot in list.Shots)
        {
            if (string.IsNullOrWhiteSpace(shot.Name))
                throw new JsonException("every shot needs a name");
            if (!seen.Add(shot.Name))
                throw new JsonException($"duplicate shot name '{shot.Name}'");
            if (shot.SettleFrames < 1)
                throw new JsonException($"shot '{shot.Name}' has settleFrames < 1");
            if (!double.IsFinite(shot.AzimuthDegrees) || !double.IsFinite(shot.ElevationDegrees)
                || shot.ElevationDegrees < -89 || shot.ElevationDegrees > 89)
                throw new JsonException($"shot '{shot.Name}' has invalid orbit angles");

            if (shot.Focus is null)
            {
                if (shot.Distance is not null)
                    throw new JsonException($"shot '{shot.Name}' supplies distance without focus");
                continue;
            }

            if (shot.Camera != ShotCamera.Orbit
                || shot.Focus.Length != 3
                || shot.Focus.Any(value => !double.IsFinite(value))
                || shot.Distance is not { } distance || !double.IsFinite(distance) || distance <= 0)
                throw new JsonException($"shot '{shot.Name}' needs a finite 3D focus and positive distance for an orbit");
        }
        return list;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);
}
