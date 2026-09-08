using OBP.Core.Math;

namespace OBP.Composition;

/// <summary>
/// A neutral, engine-independent world-composition document: two or more
/// reconstructed OBP worlds, their composition-space transforms, and the anchor
/// pairs / comparison state used to align and study them. This is the
/// <b>authoritative persistence format</b> for a fused-world experiment — the
/// Godot presentation objects consume it, they do not replace it. It contains
/// <b>no reconstructed geometry</b>: only references to source worlds and the
/// non-destructive placement metadata.
/// </summary>
public sealed record WorldComposition
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>Optional human name ("R&amp;C1 Veldin vs UYA Veldin").</summary>
    public string? Name { get; init; }

    /// <summary>Optional free-text notes — findings, caveats, TODOs.</summary>
    public string? Notes { get; init; }

    public IReadOnlyList<WorldPlacement> Worlds { get; init; } = System.Array.Empty<WorldPlacement>();

    public IReadOnlyList<AnchorPair> Anchors { get; init; } = System.Array.Empty<AnchorPair>();

    /// <summary>
    /// Comparison-view state worth persisting between sessions: which world is
    /// soloed, whether scale fitting is enabled, global overlay opacity, etc.
    /// </summary>
    public ComparisonState Comparison { get; init; } = new();

    public WorldPlacement? World(string id) =>
        Worlds.FirstOrDefault(w => string.Equals(w.Id, id, System.StringComparison.Ordinal));

    /// <summary>Anchor pairs between a specific ordered world pair, enabled ones only.</summary>
    public IReadOnlyList<AnchorPair> AnchorsBetween(string worldAId, string worldBId) =>
        Anchors.Where(a => a.Enabled && a.WorldAId == worldAId && a.WorldBId == worldBId).ToList();

    /// <summary>Distinct ordered <c>(A, B)</c> world-id pairs that have at least one enabled anchor.</summary>
    public IReadOnlyList<(string A, string B)> AnchoredWorldPairs() =>
        Anchors.Where(a => a.Enabled)
            .Select(a => (a.WorldAId, a.WorldBId))
            .Distinct()
            .ToList();

    /// <summary>
    /// Solve the rigid (or, if <paramref name="allowScale"/>, uniform-scale)
    /// planar alignment carrying <paramref name="worldBId"/> onto
    /// <paramref name="worldAId"/> from their shared anchors, and return a copy of
    /// this composition with world B's transform replaced. World A is untouched;
    /// no geometry is modified. The <see cref="PlanarAlignmentResult"/> (transform,
    /// fit quality, residuals) is returned alongside for reporting.
    /// </summary>
    public (WorldComposition Composition, PlanarAlignmentResult Result) SolveAlignment(
        string worldAId, string worldBId, bool allowScale = false)
    {
        var result = PlanarAlignmentSolver.Solve(AnchorsBetween(worldAId, worldBId), allowScale);

        // World B is placed relative to world A's current transform: compose so
        // that B's anchors land on A's anchors *in composition space*.
        var worldA = World(worldAId);
        var baseTransform = worldA?.Transform ?? CompositionTransform.Identity;
        var composed = baseTransform.IsIdentity ? result.Transform : result.Transform.Then(baseTransform);

        var updated = Worlds
            .Select(w => w.Id == worldBId ? w.WithTransform(composed.Rounded()) : w)
            .ToList();

        return (this with { Worlds = updated }, result);
    }

    public WorldComposition AddWorld(WorldPlacement world)
    {
        if (Worlds.Any(w => w.Id == world.Id))
        {
            throw new System.ArgumentException($"a world with id '{world.Id}' is already in the composition", nameof(world));
        }

        return this with { Worlds = Worlds.Append(world).ToList() };
    }

    public WorldComposition AddAnchor(AnchorPair anchor) =>
        this with { Anchors = Anchors.Append(anchor).ToList() };

    /// <summary>Deterministic ordering + rounding, so serialization is byte-stable regardless of edit order.</summary>
    public WorldComposition Canonical()
    {
        var worlds = Worlds
            .OrderBy(w => w.Id, System.StringComparer.Ordinal)
            .Select(w => w with { Transform = w.Transform.Rounded() })
            .ToList();

        var anchors = Anchors
            .OrderBy(a => a.WorldAId, System.StringComparer.Ordinal)
            .ThenBy(a => a.WorldBId, System.StringComparer.Ordinal)
            .ThenBy(a => a.Label ?? string.Empty, System.StringComparer.Ordinal)
            .ThenBy(a => a.LocalA.X).ThenBy(a => a.LocalA.Y).ThenBy(a => a.LocalA.Z)
            .Select(a => a with
            {
                LocalA = Round(a.LocalA),
                LocalB = Round(a.LocalB),
            })
            .ToList();

        return this with { Version = CurrentVersion, Worlds = worlds, Anchors = anchors };

        static Vec3 Round(Vec3 v) => new(
            System.Math.Round(v.X, 6), System.Math.Round(v.Y, 6), System.Math.Round(v.Z, 6));
    }
}

/// <summary>Persisted comparison / overlay view state.</summary>
public sealed record ComparisonState
{
    /// <summary>Id of the world currently soloed in the lab, or null for "show all".</summary>
    public string? SoloWorldId { get; init; }

    /// <summary>Id of the world the composition controls are currently editing.</summary>
    public string? ActiveWorldId { get; init; }

    /// <summary>Whether the alignment solver should fit uniform scale (default: rigid, no scale).</summary>
    public bool FitScale { get; init; }

    /// <summary>Overlay mode: "both", "a-only", "b-only" — a hint for the capture / lab default view.</summary>
    public string OverlayMode { get; init; } = "both";
}
