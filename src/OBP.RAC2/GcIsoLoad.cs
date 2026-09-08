using OBP.Core;
using OBP.IO;
using OBP.PS2;
using OBP.Runtime;

namespace OBP.RAC2;

/// <summary>
/// The Godot-free "open a Going Commando disc image" façade the native file
/// picker (and <c>obp test-import</c>) sit on top of: a fast supported-build
/// check from the boot serial, a one-call identify + level import, and the slow
/// exact-hash authority verification (streamed, cancellable — run it on a
/// background thread).
/// </summary>
public static class GcIsoLoad
{
    public sealed record Identity(
        bool Supported,
        string? DiscSerial,
        string ExpectedSerial,
        string BuildId,
        long SizeBytes,
        long ExpectedSizeBytes,
        string? Problem)
    {
        public bool SizeMatches => SizeBytes == ExpectedSizeBytes;
    }

    public sealed record LoadResult(Identity Identity, int Level, RuntimeWorld World);

    /// <summary>Fast check — reads only the boot executable name (a few KB). No hashing.</summary>
    public static Identity Identify(IRandomAccessReader iso)
    {
        string expectedSerial = Rac2Authority.Primary.Serial!;
        const long expectedSize = Rac2Authority.PrimaryIsoSizeBytes;

        try
        {
            var boot = Ps2Boot.ReadBootInfo(iso);
            bool serialOk = string.Equals(boot.Serial, expectedSerial, StringComparison.OrdinalIgnoreCase);
            string? problem = serialOk
                ? null
                : boot.Serial is null
                    ? "No PlayStation 2 boot executable found — this is not a PS2 disc image."
                    : $"Disc serial {boot.Serial} is not the supported Going Commando build ({expectedSerial}).";
            return new Identity(serialOk, boot.Serial, expectedSerial, Rac2Authority.Primary.BuildId,
                iso.Length, expectedSize, problem);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or NotSupportedException)
        {
            return new Identity(false, null, expectedSerial, Rac2Authority.Primary.BuildId,
                iso.Length, expectedSize, $"Could not read a PS2 disc image: {ex.Message}");
        }
    }

    /// <summary>Identify then import one level. Throws <see cref="InvalidDataException"/> for an unsupported disc.</summary>
    public static LoadResult LoadLevel(IRandomAccessReader iso, int level)
    {
        var identity = Identify(iso);
        if (!identity.Supported)
        {
            throw new InvalidDataException(identity.Problem ?? "Unsupported disc image.");
        }

        return new LoadResult(identity, level, GcWorldImport.Build(iso, level));
    }

    /// <summary>The authority manifest for the primary GC build, built from the committed checksums (no file needed).</summary>
    public static AuthorityManifest PrimaryManifest() => new(
        Game: "rac2",
        BuildId: Rac2Authority.Primary.BuildId,
        Region: Rac2Authority.Primary.Region,
        Serial: Rac2Authority.Primary.Serial,
        Revision: Rac2Authority.Primary.Revision,
        PayloadSizeBytes: Rac2Authority.PrimaryIsoSizeBytes,
        PayloadSha256: Rac2Authority.Primary.Sha256!);

    /// <summary>
    /// Full authority verification: exact size then streamed SHA-256 against the
    /// known-good dump. Slow (multi-gigabyte); pass a <paramref name="progress"/>
    /// and run it off the main thread. Throws on any mismatch.
    /// </summary>
    public static ObpBuildIdentity Verify(
        IRandomAccessReader iso,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        Ps2BuildIdentification.IdentifyAndVerify(iso, PrimaryManifest(), progress, cancellationToken);
}
