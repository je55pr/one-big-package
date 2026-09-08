using OBP.Core;

namespace OBP.RAC2;

/// <summary>
/// The primary authority build for native Going Commando world import — NTSC-U
/// v1.01. Mirrors <c>reference-ts/packages/importer-rac2</c>'s
/// <c>RAC2_PRIMARY_AUTHORITY</c> and the manifest in
/// <c>research/manifests/</c>. Retail data itself is never committed.
/// </summary>
public static class Rac2Authority
{
    public static readonly ObpBuildIdentity Primary = new(
        Game: ObpSourceGame.Rac2,
        BuildId: "rac2-ntscu-v1.01",
        Region: "NTSC-U",
        Serial: "SCUS-97268",
        Revision: "1.01 original retail",
        Sha256: "9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5");

    /// <summary>Exact byte length of the authority ISO (from <c>research/manifests/rac2-ntscu-v1.01.json</c>).</summary>
    public const long PrimaryIsoSizeBytes = 3_828_350_976L;

    /// <summary>Build ids whose native layout the pinned GC decoders are known to match.</summary>
    public static readonly IReadOnlySet<string> SupportedBuildIds = new HashSet<string> { Primary.BuildId };
}
