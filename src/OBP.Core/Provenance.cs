namespace OBP.Core;

/// <summary>
/// Which source game a piece of data came from. Mirrors the TypeScript
/// <c>OBPSourceGame</c>. "synthetic" covers test fixtures.
/// </summary>
public enum ObpSourceGame
{
    Rac1,
    Rac2,
    Rac3,
    Deadlocked,
    Synthetic,
}

/// <summary>
/// Provenance for a decoded asset: which game / build it came from and, where
/// meaningful, the native level and asset identity. Every decoded structure
/// carries one so OBP-created data never gets confused with recovered native
/// data. Mirrors the TypeScript <c>OBPSourceRef</c>.
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
