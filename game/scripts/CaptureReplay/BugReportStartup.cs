using System.Text.Json;
using Godot;
using OBP.Fusion;
using OBP.Godot;

namespace OBP.Game;

internal sealed record BugReportStartupResult(bool Ready, CommandLineArgs Args, BugReportCapture? Capture);

internal static class BugReportStartup
{
    internal static BugReportStartupResult Initialize(Node host, CommandLineArgs args, CancellationToken token)
    {
        try
        {
            var capture = BugReportCapture.Prepare(args, new(ProjectSettings.GlobalizePath("res://../captures/bug-reports"),
                ProjectSettings.GlobalizePath("user://obp-profile.json"), ProjectSettings.GlobalizePath("user://rac1-campaign.json")), token);
            return new(true, capture?.Args ?? args, capture);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException or UnsupportedProfileVersionException)
        {
            GD.PrintErr("[bug-report] startup failed: " + error.Message);
            if (args.BugCapture && !args.BugReportSmoke && DisplayServer.GetName() != "headless")
                OS.Alert(error.Message, "OBP Bug Capture could not start");
            host.GetNodeOrNull<Node>("SourceManagerBootstrap")?.Free();
            try
            {
                if (args.PlaytestReport is { } output)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                    File.WriteAllText(output, JsonSerializer.Serialize(new { Result = "invalid", Error = error.Message }));
                }
            }
            catch (Exception reportError) when (reportError is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
            { GD.PrintErr("[bug-report] startup report unavailable: " + reportError.Message); }
            finally { ApplicationLifecycle.RequestQuit(host, "bug-report-invalid", 2); }
            return new(false, args, null);
        }
    }
}
