using OBP.Core;

namespace OBP.RAC1;

/// <summary>
/// R&amp;C1 authority build (see <c>research/manifests/rac1-ntscu.json</c>).
/// Native world import is promoted incrementally from retail-validated reference
/// archaeology; unsupported sections remain absent rather than inferred.
/// </summary>
public static class Rac1Authority
{
    public static readonly ObpBuildIdentity Primary = new(
        Game: ObpSourceGame.Rac1,
        BuildId: "rac1-ntscu-original",
        Region: "NTSC-U",
        Serial: "SCUS-97199",
        Revision: "original retail",
        Sha256: "ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d");

    /// <summary>Exact byte length of the authority ISO.</summary>
    public const long PrimaryIsoSizeBytes = 4_214_095_872L;
}
