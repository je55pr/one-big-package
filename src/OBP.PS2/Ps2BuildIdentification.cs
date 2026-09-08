using OBP.Core;
using OBP.IO;

namespace OBP.PS2;

public static class Ps2BuildIdentification
{
    /// <summary>
    /// Full "supported build" check for a complete PS2 disc source:
    /// ISO-9660 → SYSTEM.CNF → boot serial, cross-checked against the manifest,
    /// then exact payload size + SHA-256 verification. Returns the verified
    /// identity only when everything agrees.
    /// </summary>
    public static ObpBuildIdentity IdentifyAndVerify(
        IRandomAccessReader source,
        AuthorityManifest manifest,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var boot = Ps2Boot.ReadBootInfo(source);
        if (manifest.Serial is not null && boot.Serial is not null && boot.Serial != manifest.Serial)
        {
            throw new InvalidDataException(
                $"Disc serial {boot.Serial} does not match authority {manifest.BuildId} ({manifest.Serial}).");
        }

        return BuildVerification.Verify(source, manifest, progress, cancellationToken);
    }
}
