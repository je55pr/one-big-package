namespace OBP.Composition;

/// <summary>
/// One reconstructed world inside a composition. It carries only a
/// <em>reference</em> to the neutral source world (never its geometry) plus the
/// composition-space transform and the non-destructive display state the lab can
/// toggle. The referenced world is loaded through the normal
/// game-specific → <c>RuntimeWorld</c> pipeline and is never mutated.
/// </summary>
public sealed record WorldPlacement
{
    /// <summary>Stable id, unique within a <see cref="WorldComposition"/> (e.g. "rc1-veldin", "uya-veldin", or "uya-veldin-2" for a second copy).</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Canonical neutral destination id when one exists (the <c>chatgpt/obp</c>
    /// provider registry key, e.g. <c>rac1:...</c> / <c>rac3:...</c>). Optional
    /// so the lab still works against the current level-id load path.
    /// </summary>
    public string? DestinationId { get; init; }

    /// <summary>Source game tag mirroring <c>RuntimeWorld.Game</c> ("Going Commando", "R&amp;C1", "Up Your Arsenal").</summary>
    public string? SourceGame { get; init; }

    /// <summary>Build id mirroring <c>RuntimeWorld.BuildId</c> — provenance for the reconstruction this placement was solved against.</summary>
    public string? BuildId { get; init; }

    /// <summary>Native level id for the current <c>GcWorldImport.Build(reader, levelId)</c> path (until a provider registry lands on main).</summary>
    public int? LevelId { get; init; }

    /// <summary>Free-text label for the developer UI.</summary>
    public string? Label { get; init; }

    /// <summary>The transform root above this world's generated scene.</summary>
    public CompositionTransform Transform { get; init; } = CompositionTransform.Identity;

    /// <summary>Whole-world visibility toggle.</summary>
    public bool Visible { get; init; } = true;

    /// <summary>Whole-world opacity for overlay comparison, 0..1 (1 = opaque). Applied non-destructively at the presentation layer.</summary>
    public double Opacity { get; init; } = 1.0;

    /// <summary>Optional debug tint (RGB 0..1) mixed over the world for A/B distinction. Null = untinted.</summary>
    public double[]? DebugTint { get; init; }

    /// <summary>
    /// Per-asset-kind visibility overrides ("tfrag", "tie", "shrub", "moby",
    /// "sky", "collision", "markers"). A kind absent from the map is visible.
    /// Neutral <c>RuntimeMesh.AssetKind</c> values — no game-specific keys.
    /// </summary>
    public IReadOnlyDictionary<string, bool>? CategoryVisibility { get; init; }

    public bool CategoryVisible(string kind) =>
        CategoryVisibility is null || !CategoryVisibility.TryGetValue(kind, out var v) || v;

    public WorldPlacement WithTransform(CompositionTransform t) => this with { Transform = t };
}
