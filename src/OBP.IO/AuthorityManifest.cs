using System.Text.Json;
using OBP.Core;

namespace OBP.IO;

/// <summary>
/// A canonical retail authority manifest (<c>research/manifests/*.json</c>).
/// Only the fields OBP verifies against are read; the raw JSON is not committed
/// anywhere, only the checksums / sizes / serials.
/// </summary>
public sealed record AuthorityManifest(
    string Game,
    string BuildId,
    string? Region,
    string? Serial,
    string? Revision,
    long PayloadSizeBytes,
    string PayloadSha256)
{
    public static AuthorityManifest Load(string jsonPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var r = doc.RootElement;
        var payload = r.GetProperty("payload");
        return new AuthorityManifest(
            Game: r.GetProperty("game").GetString() ?? throw Missing("game", jsonPath),
            BuildId: r.GetProperty("buildId").GetString() ?? throw Missing("buildId", jsonPath),
            Region: r.TryGetProperty("region", out var region) ? region.GetString() : null,
            Serial: r.TryGetProperty("serial", out var serial) ? serial.GetString() : null,
            Revision: r.TryGetProperty("revision", out var revision) ? revision.GetString() : null,
            PayloadSizeBytes: payload.GetProperty("sizeBytes").GetInt64(),
            PayloadSha256: (payload.GetProperty("sha256").GetString() ?? throw Missing("payload.sha256", jsonPath)).ToLowerInvariant());
    }

    public ObpSourceGame ResolveGame() => Game switch
    {
        "rac1" => ObpSourceGame.Rac1,
        "rac2" => ObpSourceGame.Rac2,
        "rac3" => ObpSourceGame.Rac3,
        "deadlocked" => ObpSourceGame.Deadlocked,
        _ => throw new InvalidOperationException($"Unsupported native OBP source game '{Game}' in authority manifest."),
    };

    private static Exception Missing(string field, string path) => new InvalidDataException($"Manifest {path} is missing '{field}'.");
}

public static class BuildVerification
{
    /// <summary>
    /// Verify a complete logical source against its authority manifest. No build
    /// identity is returned until both payload size and SHA-256 match. Hashing is
    /// bounded / streamed. Mirrors the TypeScript <c>verifyRawIsoSplitSource</c>.
    /// </summary>
    public static ObpBuildIdentity Verify(
        IRandomAccessReader source,
        AuthorityManifest manifest,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (source.Length != manifest.PayloadSizeBytes)
        {
            throw new InvalidDataException(
                $"Source size mismatch for {manifest.BuildId}: expected {manifest.PayloadSizeBytes}, got {source.Length}.");
        }

        string sha = Hashing.Sha256Hex(source, progress: progress, cancellationToken: cancellationToken);
        if (!string.Equals(sha, manifest.PayloadSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Source SHA-256 mismatch for {manifest.BuildId}: expected {manifest.PayloadSha256}, got {sha}.");
        }

        return new ObpBuildIdentity(
            Game: manifest.ResolveGame(),
            BuildId: manifest.BuildId,
            Region: manifest.Region,
            Serial: manifest.Serial,
            Revision: manifest.Revision,
            Sha256: sha);
    }
}
