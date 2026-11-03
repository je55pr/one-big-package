namespace OBP.Core;

/// <summary>
/// Which source game a piece of data came from. "synthetic" covers test fixtures.
/// </summary>
public enum ObpSourceGame
{
    Rac1,
    Rac2,
    Rac3,
    Deadlocked,
    Synthetic,
}

/// <summary>Classification helpers for <see cref="ObpSourceGame"/>.</summary>
public static class ObpSourceGameExtensions
{
    /// <summary>
    /// True for the three retail Ratchet &amp; Clank trilogy sources (Rac1, Rac2, Rac3) that carry native
    /// actor, weapon, effect and profile identities. False for Deadlocked, Synthetic and undefined values.
    /// </summary>
    public static bool IsTrilogySource(this ObpSourceGame game) => game is ObpSourceGame.Rac1 or ObpSourceGame.Rac2 or ObpSourceGame.Rac3;
}

/// <summary>
/// Provenance for a decoded asset: which game / build it came from and, where
/// meaningful, the native level and asset identity. Every decoded structure
/// carries one so OBP-created data never gets confused with recovered native
/// data.
/// </summary>
public sealed record ObpSourceRef(
    ObpSourceGame Game,
    string BuildId,
    string? LevelId = null,
    string? AssetKind = null,
    string? OriginalId = null);

/// <summary>
/// A supported retail build identity. Verified against the manifests in
/// <c>research/manifests/</c>.
/// </summary>
public sealed record ObpBuildIdentity(
    ObpSourceGame Game,
    string BuildId,
    string? Region = null,
    string? Serial = null,
    string? Revision = null,
    string? Sha256 = null);
