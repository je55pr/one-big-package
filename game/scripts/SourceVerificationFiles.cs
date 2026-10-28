using OBP.Core;
using OBP.IO;
using OBP.RAC2;

namespace OBP.Game;

internal static class SourceVerificationFiles
{
    internal static VerificationSourceStamp? Stamp(ObpAttachedSource? source)
    {
        if (source is null) return null;
        try
        {
            var file = new FileInfo(source.Path);
            return file.Exists ? new(source.Identity.Game, source.Identity.BuildId, source.Path, file.Length, file.LastWriteTimeUtc.Ticks) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    internal static string VerifyRac2(VerificationSourceStamp source, IProgress<double> progress, CancellationToken cancellation)
    {
        try
        {
            using var reader = new FileRandomAccessReader(source.Path);
            var verified = Rac2IsoLoad.Verify(reader, progress, cancellation);
            return $"✓ authority build verified — {verified.Serial} {verified.Revision}";
        }
        catch (InvalidDataException error)
        { return $"⚠ disc does not match the known-good dump: {error.Message}"; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return $"⚠ could not read the disc image: {error.Message}"; }
    }
}
