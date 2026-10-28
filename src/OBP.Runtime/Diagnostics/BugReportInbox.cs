using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OBP.Core;

namespace OBP.Runtime.Diagnostics;

public sealed record BugReportEntry(Guid Id, DateTimeOffset CreatedUtc, int Step, string Marker,
    string Destination, string Title, string Description);

public sealed record BugReportRecording(string FileName, long Bytes, string Sha256, int Steps,
    string Destination, string SourceBuild, Guid AssemblyId, IReadOnlyDictionary<string, Guid> Assemblies,
    string EngineVersion, string Platform, string PhysicsEngine, string ApplicationVersion = "")
{
    public int CaptureVersion { get; init; } = PlaytestRunHeader.LegacyVersion;
    public IReadOnlyList<ObpBuildIdentity> Sources { get; init; } = [];
    public IReadOnlyList<PlaytestAdapter> Adapters { get; init; } = [];
}

/// <summary>One atomic index and one shared, completed recording per reporting session.</summary>
public sealed record BugReportSession(int Version, Guid Id, DateTimeOffset StartedUtc,
    BugReportRecording Recording, IReadOnlyList<BugReportEntry> Reports);

/// <summary>
/// Local, payload-free report storage. Only validated completed tapes are published.
/// Paths/logs/assets are not manifest fields; optional prose is authored by the player.
/// </summary>
public sealed class BugReportInbox
{
    public const long MaximumArchiveBytes = 25_000_000;
    public const int MaximumReports = 256;
    private const int MaximumManifestBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private sealed record PendingDraft(int Version, Guid SessionId, BugReportEntry Report);
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string Root { get; }

    public BugReportInbox(string root) => Root = Path.GetFullPath(root);
    public string SessionDirectory(Guid id)
    {
        if (id == Guid.Empty) throw new InvalidDataException("Missing report session identity.");
        string directory = Path.Combine(Root, id.ToString("N"));
        if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Report session must be an ordinary local directory.");
        return directory;
    }

    public async Task<BugReportSession> SaveAsync(Guid sessionId, PlaytestRunWriter writer, BugReportEntry report)
    {
        ValidateReport(report);
        await _gate.WaitAsync().ConfigureAwait(false);
        string? snapshot = null;
        bool published = false;
        try
        {
            string directory = SessionDirectory(sessionId);
            Directory.CreateDirectory(directory);
            var previous = File.Exists(Path.Combine(directory, "session.json")) ? Read(sessionId) : null;
            if (previous?.Reports.Any(r => r.Id == report.Id || r.Marker == report.Marker) == true)
                throw new InvalidDataException("This report has already been saved.");
            if (previous?.Reports.Count >= MaximumReports) throw new InvalidDataException("This session has reached its report limit.");
            snapshot = Path.Combine(directory, "run-" + Guid.NewGuid().ToString("N") + ".run.jsonl.gz");
            await writer.ExportSnapshotAsync(snapshot).ConfigureAwait(false);
            var session = BuildSession(sessionId, previous, report, snapshot);
            WriteAtomic(Path.Combine(directory, "session.json"), session);
            published = true;
            // The single index now points every report at the newer complete
            // prefix. An in-use old file may remain until the session is deleted.
            if (previous is not null) TryDelete(Path.Combine(directory, previous.Recording.FileName));
            TryDelete(DraftPath(sessionId, report.Id));
            return session;
        }
        finally
        {
            if (!published && snapshot is not null) TryDelete(snapshot);
            _gate.Release();
        }
    }

    /// <summary>Persist submitted prose before its save may outlive the form.</summary>
    public void RetainPendingDraft(Guid sessionId, BugReportEntry report)
    {
        ValidateReport(report);
        string directory = SessionDirectory(sessionId);
        Directory.CreateDirectory(directory);
        string path = DraftPath(sessionId, report.Id);
        if (!File.Exists(path) && Directory.EnumerateFiles(directory, "draft-*.json").Take(MaximumReports).Count() >= MaximumReports)
            throw new InvalidDataException("This session has reached its pending draft limit.");
        WriteAtomicBytes(path, JsonSerializer.SerializeToUtf8Bytes(new PendingDraft(1, sessionId, report), Json));
    }

    public int PendingDraftCount() => PendingDraftFiles().Count();

    /// <summary>
    /// Restore submitted reports from the original capture's completed tape.
    /// Incomplete or inaccessible captures keep their drafts for a later retry.
    /// Never use a new session's writer or rewrite the recorded input stream.
    /// </summary>
    public int RestorePendingDrafts(CancellationToken cancellationToken = default)
    {
        int restored = 0;
        foreach (string draftPath in PendingDraftFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? snapshot = null;
            bool published = false;
            try
            {
                if (new FileInfo(draftPath).Length > MaximumManifestBytes)
                    throw new InvalidDataException("Pending report draft is too large.");
                var draft = JsonSerializer.Deserialize<PendingDraft>(File.ReadAllBytes(draftPath), Json)
                    ?? throw new InvalidDataException("Invalid pending report draft.");
                if (draft.Report is null) throw new InvalidDataException("Pending report draft is missing its report.");
                ValidateReport(draft.Report);
                if (draft.Version != 1 || draftPath != DraftPath(draft.SessionId, draft.Report.Id))
                    throw new InvalidDataException("Pending report draft identity changed.");
                string directory = SessionDirectory(draft.SessionId);
                var previous = File.Exists(Path.Combine(directory, "session.json")) ? Read(draft.SessionId) : null;
                if (previous?.Reports.SingleOrDefault(report => report.Id == draft.Report.Id) is { } saved)
                {
                    if (saved == draft.Report) TryDelete(draftPath);
                    continue;
                }
                if (previous?.Reports.Count >= MaximumReports) continue;
                string recording = Path.Combine(Root, "live", draft.SessionId.ToString("N"), "session.run.jsonl.gz");
                if (!File.Exists(recording)) continue;
                snapshot = Path.Combine(directory, "run-" + Guid.NewGuid().ToString("N") + ".run.jsonl.gz");
                using (var input = File.OpenRead(recording))
                using (var output = new FileStream(snapshot, FileMode.CreateNew, FileAccess.Write))
                {
                    CopyBounded(input, output, PlaytestRunLimits.Compressed, cancellationToken);
                    output.Flush(flushToDisk: true);
                }
                var session = BuildSession(draft.SessionId, previous, draft.Report, snapshot, cancellationToken);
                WriteAtomic(Path.Combine(directory, "session.json"), session, cancellationToken);
                published = true;
                if (previous is not null) TryDelete(Path.Combine(directory, previous.Recording.FileName));
                TryDelete(draftPath);
                restored++;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException)
            { /* Retain the submitted draft and original capture for the next startup. */ }
            finally { if (!published && snapshot is not null) TryDelete(snapshot); }
        }
        return restored;
    }

    private IEnumerable<string> PendingDraftFiles()
    {
        if (!Directory.Exists(Root)) yield break;
        foreach (string directory in Directory.EnumerateDirectories(Root))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id)) continue;
            // Keep the same directory/link boundary as ordinary saved reports.
            string owned = SessionDirectory(id);
            foreach (string path in Directory.EnumerateFiles(owned, "draft-*.json").Take(MaximumReports)) yield return path;
        }
    }

    private string DraftPath(Guid sessionId, Guid reportId) =>
        Path.Combine(SessionDirectory(sessionId), "draft-" + reportId.ToString("N") + ".json");

    private static BugReportSession BuildSession(Guid sessionId, BugReportSession? previous, BugReportEntry report,
        string snapshot, CancellationToken cancellationToken = default)
    {
        var (header, steps, markers) = InspectRecording(snapshot, cancellationToken);
        var reports = (previous?.Reports ?? []).Append(report).ToArray();
        if (reports.Any(item => !markers.Contains(new(item.Step, item.Marker))))
            throw new InvalidDataException("A report marker is missing from its recording.");
        var recording = new BugReportRecording(Path.GetFileName(snapshot), new FileInfo(snapshot).Length,
            Hash(snapshot, cancellationToken), steps, header.Destination, header.SourceBuild, header.AssemblyId,
            header.Assemblies, header.EngineVersion, header.Platform, header.PhysicsEngine, header.ApplicationVersion)
        { CaptureVersion = header.Version, Sources = header.Sources, Adapters = header.Adapters };
        var session = new BugReportSession(1, sessionId, header.StartedUtc, recording, reports);
        Validate(session);
        return session;
    }

    public BugReportSession Read(Guid id)
    {
        var session = ReadManifest(Path.Combine(SessionDirectory(id), "session.json"));
        if (session.Id != id) throw new InvalidDataException("Report session identity changed.");
        return session;
    }

    public IReadOnlyList<(Guid Id, BugReportSession? Session, string? Error)> List()
    {
        if (!Directory.Exists(Root)) return [];
        var result = new List<(Guid, BugReportSession?, string?)>();
        foreach (string directory in Directory.EnumerateDirectories(Root))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id)) continue;
            try { result.Add((id, Read(id), null)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException)
            { result.Add((id, null, error.Message)); }
        }
        return result;
    }

    public int InterruptedCaptureCount(Guid currentSession)
    {
        string live = Path.Combine(Root, "live");
        if (!Directory.Exists(live)) return 0;
        return Directory.EnumerateDirectories(live).Count(directory =>
            Guid.TryParseExact(Path.GetFileName(directory), "N", out var id) && id != currentSession &&
            (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0 &&
            File.Exists(Path.Combine(directory, "session.run.jsonl.gz.partial")));
    }

    public (BugReportSession Session, BugReportEntry Report, string RecordingPath) Resolve(Guid sessionId, Guid reportId) =>
        Resolve(sessionId, reportId, CancellationToken.None);

    public (BugReportSession Session, BugReportEntry Report, string RecordingPath) Resolve(Guid sessionId, Guid reportId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = Read(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        var report = session.Reports.SingleOrDefault(r => r.Id == reportId)
            ?? throw new InvalidDataException("Report not found in this session.");
        string path = Path.Combine(SessionDirectory(sessionId), session.Recording.FileName);
        VerifyRecording(session, path, cancellationToken);
        return (session, report, path);
    }

    public string Export(Guid sessionId, Guid reportId, long maximumBytes = MaximumArchiveBytes) =>
        Export(sessionId, reportId, CancellationToken.None, maximumBytes);

    public string Export(Guid sessionId, Guid reportId, CancellationToken cancellationToken, long maximumBytes = MaximumArchiveBytes)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumBytes is <= 0 or > MaximumArchiveBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var (session, report, path) = Resolve(sessionId, reportId, cancellationToken);
        var selected = session with { Reports = [report] };
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(selected, Json);
        if (session.Recording.Bytes + manifest.Length + 8192 > maximumBytes)
            throw new IOException("Saved locally. This recording is too large for a 25 MB portable report.");
        string exports = Path.Combine(Root, "exports");
        Directory.CreateDirectory(exports);
        string target = Path.Combine(exports, reportId.ToString("N") + ".obp-bug.zip");
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var output = zip.CreateEntry("report.json").Open()) output.Write(manifest);
                using var input = File.OpenRead(path);
                using var recording = zip.CreateEntry(session.Recording.FileName, CompressionLevel.NoCompression).Open();
                CopyBounded(input, recording, maximumBytes, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (new FileInfo(temporary).Length > maximumBytes) throw new IOException("Portable report exceeds its size limit.");
            File.Move(temporary, target, overwrite: true);
            return target;
        }
        finally { TryDelete(temporary); }
    }

    public BugReportSession Import(string archive) => Import(archive, CancellationToken.None);

    public BugReportSession Import(string archive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (new FileInfo(archive).Length > MaximumArchiveBytes) throw new InvalidDataException("Portable report exceeds 25 MB.");
        using var zip = ZipFile.OpenRead(archive);
        return ImportArchive(zip, cancellationToken);
    }

    private BugReportSession ImportArchive(ZipArchive zip, CancellationToken cancellationToken)
    {
        if (zip.Entries.Count != 2) throw new InvalidDataException("A portable report must contain only its manifest and recording.");
        var manifestEntry = zip.GetEntry("report.json") ?? throw new InvalidDataException("Report manifest missing.");
        using var buffer = new MemoryStream();
        using (var input = manifestEntry.Open()) CopyBounded(input, buffer, MaximumManifestBytes, cancellationToken);
        var session = JsonSerializer.Deserialize<BugReportSession>(buffer.ToArray(), Json)
            ?? throw new InvalidDataException("Invalid report manifest.");
        Validate(session);
        var recording = zip.GetEntry(session.Recording.FileName) ?? throw new InvalidDataException("Report recording missing.");
        // Imports get a fresh local session identity; original report IDs remain
        // stable. Never replace another developer's local session.
        session = session with { Id = Guid.NewGuid() };
        string directory = SessionDirectory(session.Id);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, session.Recording.FileName);
        try
        {
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            using (var input = recording.Open()) CopyBounded(input, output, MaximumArchiveBytes, cancellationToken);
            VerifyRecording(session, path, cancellationToken);
            WriteAtomic(Path.Combine(directory, "session.json"), session, cancellationToken);
            return session;
        }
        catch
        {
            TryDelete(path);
            // Non-recursive: remove only this failed import's empty directory.
            // Cleanup must not replace the original copy/verification error.
            try { Directory.Delete(directory); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    public void Delete(Guid sessionId)
    {
        // Only files named by the validated manifest and our own run snapshots
        // are removed. Leave unrelated files in this directory alone.
        _ = Read(sessionId);
        string directory = SessionDirectory(sessionId);
        string live = Path.Combine(Root, "live", sessionId.ToString("N"));
        if (Directory.Exists(live))
        {
            if ((File.GetAttributes(live) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Live recording directory is a link.");
            string partial = Path.Combine(live, "session.run.jsonl.gz.partial");
            // Check before removing anything: an active writer does not share
            // write/delete access. An unlocked interrupted run may be deleted.
            if (File.Exists(partial)) using (File.Open(partial, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            foreach (string name in new[] { "session.run.jsonl.gz", "session.run.jsonl.gz.partial", "campaign.json" })
                File.Delete(Path.Combine(live, name));
            if (!Directory.EnumerateFileSystemEntries(live).Any()) Directory.Delete(live);
        }
        foreach (string file in Directory.EnumerateFiles(directory))
            if (Path.GetFileName(file) == "session.json" || ValidRunName(Path.GetFileName(file)) || ValidDraftName(Path.GetFileName(file))) File.Delete(file);
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }

    private static BugReportSession ReadManifest(string path)
    {
        if (new FileInfo(path).Length > MaximumManifestBytes) throw new InvalidDataException("Report manifest is too large.");
        var session = JsonSerializer.Deserialize<BugReportSession>(File.ReadAllBytes(path), Json)
            ?? throw new InvalidDataException("Invalid report manifest.");
        Validate(session);
        return session;
    }

    private static void VerifyRecording(BugReportSession session, string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (new FileInfo(path).Length != session.Recording.Bytes || Hash(path, cancellationToken) != session.Recording.Sha256)
            throw new InvalidDataException("Recording size/hash does not match its report; it may be incomplete or changed.");
        var (header, steps, markers) = InspectRecording(path, cancellationToken);
        var info = session.Recording;
        if (steps != info.Steps || header.AssemblyId != info.AssemblyId || header.SourceBuild != info.SourceBuild ||
            header.Destination != info.Destination || header.EngineVersion != info.EngineVersion ||
            header.Platform != info.Platform || header.PhysicsEngine != info.PhysicsEngine || header.ApplicationVersion != info.ApplicationVersion ||
            !header.MatchesAssemblies(info.Assemblies) || info.CaptureVersion != header.Version ||
            !info.Sources.SequenceEqual(header.Sources) || !info.Adapters.SequenceEqual(header.Adapters) ||
            session.Reports.Any(r => !markers.Contains(new(r.Step, r.Marker))))
            throw new InvalidDataException("Recording identity or marker does not match its report.");
    }

    private static (PlaytestRunHeader Header, int Steps, IReadOnlyList<PlaytestMarker> Markers) InspectRecording(string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = new PlaytestRunReader(path);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.ReadNextStep() is null) break;
        }
        if (reader.End?.Reason is not ("saved" or "closed")) throw new InvalidDataException("Recording was not completed successfully.");
        return (reader.Header, reader.End.Steps, reader.Markers);
    }

    private static bool ValidRunName(string name) => name is { Length: 49 } && name.StartsWith("run-", StringComparison.Ordinal) &&
        name.EndsWith(".run.jsonl.gz", StringComparison.Ordinal) && Guid.TryParseExact(name.AsSpan(4, 32), "N", out _);

    private static bool ValidDraftName(string name) => name is { Length: 43 } && name.StartsWith("draft-", StringComparison.Ordinal) &&
        name.EndsWith(".json", StringComparison.Ordinal) && Guid.TryParseExact(name.AsSpan(6, 32), "N", out _);

    private static void Validate(BugReportSession session)
    {
        var recording = session.Recording;
        if (recording is { } provenance) PlaytestRunHeader.ValidateProvenance(provenance.CaptureVersion, provenance.Sources, provenance.Adapters);
        if (session.Version != 1 || session.Id == Guid.Empty || recording is null || !ValidRunName(recording.FileName) ||
            recording.Bytes is <= 0 or > PlaytestRunLimits.Compressed || recording.Steps <= 0 ||
            recording.Sha256 is not { Length: 64 } || !recording.Sha256.All(Uri.IsHexDigit) ||
            recording.AssemblyId == Guid.Empty || recording.Assemblies is null || recording.Assemblies.Count is < 1 or > 64 ||
            string.IsNullOrWhiteSpace(recording.Destination) || recording.Destination.Length > 128 ||
            string.IsNullOrWhiteSpace(recording.SourceBuild) || recording.SourceBuild.Length > 128 ||
            string.IsNullOrWhiteSpace(recording.EngineVersion) || recording.EngineVersion.Length > 128 ||
            string.IsNullOrWhiteSpace(recording.Platform) || recording.Platform.Length > 128 ||
            string.IsNullOrWhiteSpace(recording.PhysicsEngine) || recording.PhysicsEngine.Length > 128 ||
            session.Reports is null || session.Reports.Count is < 1 or > MaximumReports ||
            session.Reports.Any(r => r is null) ||
            session.Reports.Select(r => r.Id).Distinct().Count() != session.Reports.Count ||
            session.Reports.Select(r => r.Marker).Distinct().Count() != session.Reports.Count)
            throw new InvalidDataException("Invalid report session metadata.");
        foreach (var report in session.Reports)
        {
            ValidateReport(report);
            if (report.Step >= recording.Steps) throw new InvalidDataException("Report marker lies outside its recording.");
        }
    }

    private static void ValidateReport(BugReportEntry report)
    {
        if (report.Id == Guid.Empty || report.Step < 0 || string.IsNullOrWhiteSpace(report.Marker) || report.Marker.Length > 256 ||
            string.IsNullOrWhiteSpace(report.Destination) || report.Destination.Length > 128 ||
            string.IsNullOrWhiteSpace(report.Title) || report.Title.Length > 120 || report.Title.Any(char.IsControl) ||
            report.Description is null || report.Description.Length > 2000 || report.Description.Contains('\0'))
            throw new InvalidDataException("Invalid report title, description or marker.");
    }

    private static string Hash(string path, CancellationToken cancellationToken = default)
    {
        using var file = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65536];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = file.Read(buffer);
            if (count == 0) break;
            hash.AppendData(buffer.AsSpan(0, count));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void WriteAtomic(string path, BugReportSession session, CancellationToken cancellationToken = default)
        => WriteAtomicBytes(path, JsonSerializer.SerializeToUtf8Bytes(session, Json), cancellationToken);

    private static void WriteAtomicBytes(string path, byte[] json, CancellationToken cancellationToken = default)
    {
        if (json.Length > MaximumManifestBytes) throw new InvalidDataException("Report manifest is too large.");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            { file.Write(json); file.Flush(flushToDisk: true); }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { TryDelete(temporary); }
    }

    private static void CopyBounded(Stream input, Stream output, long maximum, CancellationToken cancellationToken = default)
    {
        byte[] buffer = new byte[65536];
        long total = 0;
        int count;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            count = input.Read(buffer);
            if (count == 0) break;
            cancellationToken.ThrowIfCancellationRequested();
            total += count;
            if (total > maximum) throw new InvalidDataException("Report entry exceeds its size limit.");
            output.Write(buffer, 0, count);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
