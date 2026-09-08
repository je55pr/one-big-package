using OBP.Core.Math;

namespace OBP.Composition;

/// <summary>
/// One equivalence claim between two worlds: "this point in world A is the same
/// real-world place as that point in world B" (e.g. the Ratchet garage doorway
/// in R&amp;C1 Veldin ↔ the same doorway in UYA Veldin).
///
/// <para>
/// Both positions are stored in their own world's <b>local</b> space (the
/// reconstructed <c>RuntimeWorld</c> coordinates, before any composition
/// transform). The alignment solver consumes pairs grouped by
/// <c>(WorldAId, WorldBId)</c> and reports the rigid planar transform that best
/// carries B's points onto A's, plus the residual error — it never deforms
/// geometry to force a fit.
/// </para>
/// </summary>
public sealed record AnchorPair
{
    public required string WorldAId { get; init; }

    public required string WorldBId { get; init; }

    /// <summary>Point in world A's local space (OBP Y-up).</summary>
    public required Vec3 LocalA { get; init; }

    /// <summary>Point in world B's local space (OBP Y-up).</summary>
    public required Vec3 LocalB { get; init; }

    /// <summary>Human label for the landmark ("Ratchet garage doorway", "start-area frog", "north terrain corner").</summary>
    public string? Label { get; init; }

    /// <summary>Optional: excluded from the solve but kept for reference / later re-enable.</summary>
    public bool Enabled { get; init; } = true;
}
