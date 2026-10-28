using System.Collections.Immutable;
using System.Text.Json;
using OBP.Fusion;
using OBP.IO;
using OBP.RAC1.Progression;
using OBP.Runtime.Diagnostics;

namespace OBP.Game;

internal sealed record BugReportCapturePaths(string Inbox, string Profile, string Campaign);
internal sealed record BugReportCaptureSnapshot(ImmutableArray<BugReportEntry> Reports, string RecordingPath,
    int Recordings, int Exports, ImmutableArray<string> ReplayResults);

/// <summary>Capture identity and startup preparation; closing a view never retires the store or recording.</summary>
internal sealed record BugReportCapture(CommandLineArgs Args, BugReportInbox Inbox, Guid SessionId, BugReportEntry? ReplayReport)
{
    internal static BugReportCapture? Prepare(CommandLineArgs args, BugReportCapturePaths paths, CancellationToken token)
    {
        if (!args.BugCapture && args.ReplayBugReport is null) return null;
        if (args.BugCapture && (args.PlaytestRequested || args.ReplayBugReport is not null))
            throw new InvalidDataException("Choose either bug capture or replay.");
        var inbox = new BugReportInbox(args.BugReportInbox ?? paths.Inbox);
        if (args.ReplayBugReport is { } manifest)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(manifest))!;
            if (Path.GetFileName(manifest) != "session.json" ||
                !Guid.TryParseExact(Path.GetFileName(directory), "N", out var sessionId) ||
                !Guid.TryParse(args.BugReportId, out var reportId))
                throw new InvalidDataException("Choose a saved report and its report identity.");
            inbox = new(Path.GetDirectoryName(directory)!);
            var resolved = inbox.Resolve(sessionId, reportId, token);
            return new(args with { ReplayPlaytest = resolved.RecordingPath }, inbox, Guid.Empty, resolved.Report);
        }

        inbox.RestorePendingDrafts(token);
        Guid id = Guid.NewGuid();
        string live = Path.Combine(inbox.Root, "live", id.ToString("N"));
        Directory.CreateDirectory(live);
        if (!args.NativeParityRequested)
        {
            string? destination = args.Destination;
            string profilePath = args.ProfilePath ?? paths.Profile;
            if (destination is null && !args.NoProfile && (File.Exists(profilePath) || File.Exists(profilePath + ".bak")))
            {
                var saved = AtomicDocumentFile.Load(profilePath, ObpProfile.MaximumBytes, ObpProfile.Decode,
                    error => error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException);
                if (saved.Value is not { } profile)
                    throw new InvalidDataException("The shared profile cannot be recovered; original files retained.");
                destination = profile.Destination;
            }
            args = args with { RecordPlaytest = Path.Combine(live, "session.run.jsonl.gz"), Destination = destination ?? "rac1:LEVEL0" };
        }
        else
        {
            string sourceSave = args.Rac1CampaignSavePath ?? paths.Campaign;
            int current = 0;
            string isolated = Path.Combine(live, "campaign.json");
            if (File.Exists(sourceSave))
            {
                var envelope = JsonSerializer.Deserialize<Rac1CampaignSaveEnvelope>(File.ReadAllText(sourceSave))
                    ?? throw new InvalidDataException("Campaign could not be read.");
                current = Rac1CampaignSavePolicy.RestoreOrDefault(envelope).Campaign.CurrentLevel;
                File.Copy(sourceSave, isolated);
            }
            args = args with
            {
                RecordPlaytest = Path.Combine(live, "session.run.jsonl.gz"),
                Rac1CampaignSavePath = isolated,
                Destination = args.Destination ?? $"rac1:LEVEL{current}"
            };
        }
        return new(args, inbox, id, null);
    }

    internal BugReportCaptureSnapshot ReadSnapshot()
    {
        var manifest = Inbox.Read(SessionId);
        var resolved = Inbox.Resolve(SessionId, manifest.Reports[0].Id);
        string results = Path.Combine(Inbox.Root, "replay-results");
        return new([.. manifest.Reports], resolved.RecordingPath,
            Directory.GetFiles(Inbox.SessionDirectory(SessionId), "*.gz").Length,
            Count(Path.Combine(Inbox.Root, "exports"), "*.zip"),
            Directory.Exists(results) ? [.. Directory.GetFiles(results, "*.json")] : []);
    }

    private static int Count(string path, string pattern) => Directory.Exists(path) ? Directory.GetFiles(path, pattern).Length : 0;
}
