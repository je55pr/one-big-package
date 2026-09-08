using OBP.Core;

namespace OBP.RAC3;

/// <summary>
/// Up Your Arsenal authority build (see <c>research/manifests/rac3-ntscu.json</c>).
/// Probe / identify only for now — native world import is not implemented
/// (mirrors <c>reference-ts/packages/importer-rac3</c>).
/// </summary>
public static class Rac3Authority
{
    public static readonly ObpBuildIdentity Primary = new(
        Game: ObpSourceGame.Rac3,
        BuildId: "rac3-ntscu-original",
        Region: "NTSC-U",
        Serial: "SCUS-97353",
        Revision: "original retail",
        Sha256: "d2bb15c7c5b2205db868713fc0362c2b10e87751ca5bcc4e96c1e244a8c42444");

    /// <summary>Exact byte length of the authority ISO.</summary>
    public const long PrimaryIsoSizeBytes = 4_379_377_664L;
}
