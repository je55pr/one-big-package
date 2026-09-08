using System.Text.Json;
using System.Text.Json.Serialization;
using OBP.Core.Math;

namespace OBP.Composition;

/// <summary>
/// Tiny deterministic text (JSON) persistence for a <see cref="WorldComposition"/>.
///
/// <para>
/// The format is a stable schema (see <c>docs/WORLD_COMPOSITION.md</c>): worlds
/// reference neutral destination / level ids and carry only transforms + display
/// state, anchors are world-local point pairs. No reconstructed geometry is ever
/// written. <see cref="Serialize"/> canonicalises first (sorted, rounded) so the
/// same logical composition always produces byte-identical output.
/// </para>
/// </summary>
public static class CompositionJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        PropertyNameCaseInsensitive = true,
    };

    public static string Serialize(WorldComposition composition)
    {
        var dto = ToDto(composition.Canonical());
        // Normalise indentation newlines to '\n' so output is byte-stable across
        // platforms (the repo's .gitattributes keeps *.json LF anyway).
        string json = JsonSerializer.Serialize(dto, Options).Replace("\r\n", "\n");
        return json + "\n";
    }

    public static void Save(WorldComposition composition, string path) =>
        File.WriteAllText(path, Serialize(composition));

    public static WorldComposition Deserialize(string json)
    {
        var dto = JsonSerializer.Deserialize<CompositionDto>(json, Options)
            ?? throw new JsonException("composition document was null");
        return FromDto(dto).Canonical();
    }

    public static WorldComposition Load(string path) => Deserialize(File.ReadAllText(path));

    // --- DTO layer: explicit property order = deterministic serialization -----

    private static CompositionDto ToDto(WorldComposition c) => new()
    {
        Version = c.Version,
        Name = c.Name,
        Notes = c.Notes,
        Comparison = new ComparisonDto
        {
            ActiveWorldId = c.Comparison.ActiveWorldId,
            SoloWorldId = c.Comparison.SoloWorldId,
            FitScale = c.Comparison.FitScale,
            OverlayMode = c.Comparison.OverlayMode,
        },
        Worlds = c.Worlds.Select(w => new WorldDto
        {
            Id = w.Id,
            DestinationId = w.DestinationId,
            SourceGame = w.SourceGame,
            BuildId = w.BuildId,
            LevelId = w.LevelId,
            Label = w.Label,
            Transform = new TransformDto
            {
                Translation = new[] { w.Transform.TranslationX, w.Transform.TranslationY, w.Transform.TranslationZ },
                RotationYDegrees = w.Transform.RotationYDegrees,
                Scale = w.Transform.Scale,
            },
            Visible = w.Visible,
            Opacity = w.Opacity,
            DebugTint = w.DebugTint,
            CategoryVisibility = w.CategoryVisibility is null
                ? null
                : new SortedDictionary<string, bool>(w.CategoryVisibility.ToDictionary(kv => kv.Key, kv => kv.Value), StringComparer.Ordinal),
        }).ToList(),
        Anchors = c.Anchors.Select(a => new AnchorDto
        {
            WorldA = a.WorldAId,
            WorldB = a.WorldBId,
            Label = a.Label,
            LocalA = new[] { a.LocalA.X, a.LocalA.Y, a.LocalA.Z },
            LocalB = new[] { a.LocalB.X, a.LocalB.Y, a.LocalB.Z },
            Enabled = a.Enabled,
        }).ToList(),
    };

    private static WorldComposition FromDto(CompositionDto d) => new()
    {
        Version = d.Version == 0 ? WorldComposition.CurrentVersion : d.Version,
        Name = d.Name,
        Notes = d.Notes,
        Comparison = d.Comparison is { } cs
            ? new ComparisonState
            {
                ActiveWorldId = cs.ActiveWorldId,
                SoloWorldId = cs.SoloWorldId,
                FitScale = cs.FitScale,
                OverlayMode = string.IsNullOrWhiteSpace(cs.OverlayMode) ? "both" : cs.OverlayMode,
            }
            : new ComparisonState(),
        Worlds = (d.Worlds ?? new()).Select(w => new WorldPlacement
        {
            Id = w.Id ?? throw new JsonException("world entry is missing 'id'"),
            DestinationId = w.DestinationId,
            SourceGame = w.SourceGame,
            BuildId = w.BuildId,
            LevelId = w.LevelId,
            Label = w.Label,
            Transform = w.Transform is { } t
                ? new CompositionTransform(
                    Get(t.Translation, 0), Get(t.Translation, 1), Get(t.Translation, 2),
                    t.RotationYDegrees, t.Scale == 0 ? 1.0 : t.Scale)
                : CompositionTransform.Identity,
            Visible = w.Visible,
            Opacity = w.Opacity <= 0 ? 1.0 : w.Opacity,
            DebugTint = w.DebugTint,
            CategoryVisibility = w.CategoryVisibility is { Count: > 0 } cv
                ? new Dictionary<string, bool>(cv, StringComparer.Ordinal)
                : null,
        }).ToList(),
        Anchors = (d.Anchors ?? new()).Select(a => new AnchorPair
        {
            WorldAId = a.WorldA ?? throw new JsonException("anchor entry is missing 'worldA'"),
            WorldBId = a.WorldB ?? throw new JsonException("anchor entry is missing 'worldB'"),
            LocalA = Vec(a.LocalA),
            LocalB = Vec(a.LocalB),
            Label = a.Label,
            Enabled = a.Enabled,
        }).ToList(),
    };

    private static double Get(double[]? a, int i) => a is not null && i < a.Length ? a[i] : 0;

    private static Vec3 Vec(double[]? a) => new(Get(a, 0), Get(a, 1), Get(a, 2));

    private sealed class CompositionDto
    {
        [JsonPropertyOrder(0)] public int Version { get; set; }

        [JsonPropertyOrder(1)] public string? Name { get; set; }

        [JsonPropertyOrder(2)] public string? Notes { get; set; }

        [JsonPropertyOrder(3)] public ComparisonDto? Comparison { get; set; }

        [JsonPropertyOrder(4)] public List<WorldDto>? Worlds { get; set; }

        [JsonPropertyOrder(5)] public List<AnchorDto>? Anchors { get; set; }
    }

    private sealed class ComparisonDto
    {
        [JsonPropertyOrder(0)] public string? ActiveWorldId { get; set; }

        [JsonPropertyOrder(1)] public string? SoloWorldId { get; set; }

        [JsonPropertyOrder(2)] public bool FitScale { get; set; }

        [JsonPropertyOrder(3)] public string? OverlayMode { get; set; }
    }

    private sealed class WorldDto
    {
        [JsonPropertyOrder(0)] public string? Id { get; set; }

        [JsonPropertyOrder(1)] public string? DestinationId { get; set; }

        [JsonPropertyOrder(2)] public string? SourceGame { get; set; }

        [JsonPropertyOrder(3)] public string? BuildId { get; set; }

        [JsonPropertyOrder(4)] public int? LevelId { get; set; }

        [JsonPropertyOrder(5)] public string? Label { get; set; }

        [JsonPropertyOrder(6)] public TransformDto? Transform { get; set; }

        [JsonPropertyOrder(7)] public bool Visible { get; set; } = true;

        [JsonPropertyOrder(8)] public double Opacity { get; set; } = 1.0;

        [JsonPropertyOrder(9)] public double[]? DebugTint { get; set; }

        [JsonPropertyOrder(10)] public IDictionary<string, bool>? CategoryVisibility { get; set; }
    }

    private sealed class TransformDto
    {
        [JsonPropertyOrder(0)] public double[] Translation { get; set; } = { 0, 0, 0 };

        [JsonPropertyOrder(1)] public double RotationYDegrees { get; set; }

        [JsonPropertyOrder(2)] public double Scale { get; set; } = 1.0;
    }

    private sealed class AnchorDto
    {
        [JsonPropertyOrder(0)] public string? WorldA { get; set; }

        [JsonPropertyOrder(1)] public string? WorldB { get; set; }

        [JsonPropertyOrder(2)] public string? Label { get; set; }

        [JsonPropertyOrder(3)] public double[]? LocalA { get; set; }

        [JsonPropertyOrder(4)] public double[]? LocalB { get; set; }

        [JsonPropertyOrder(5)] public bool Enabled { get; set; } = true;
    }
}
