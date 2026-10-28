using System.Text.Json;

namespace OBP.Game;

internal sealed record BugReportReplayRequest(string ResultPath, IReadOnlyList<string> Arguments);
internal sealed record BugReportReplayResult(string Outcome);
internal sealed record BugReportProcessPorts(Func<IReadOnlyList<string>, int> Start, Func<int, bool> Running,
    Action<int> Stop, Func<CancellationToken, Task> Frame, Func<ulong> Milliseconds);
internal sealed class BugReportChildCleanupException(string message) : IOException(message);

/// <summary>A reproduction child is owned until exit, including cancellation and failed result reads.</summary>
internal static class BugReportReplayProcess
{
    internal static async Task<BugReportReplayResult> RunAsync(BugReportReplayRequest request, BugReportProcessPorts ports,
        CancellationToken token)
    {
        int process = -1;
        bool completed = false;
        try
        {
            token.ThrowIfCancellationRequested();
            process = ports.Start(request.Arguments);
            if (process < 0) throw new IOException("Could not start the replay process.");
            token.ThrowIfCancellationRequested();
            ulong deadline = ports.Milliseconds() + 30 * 60 * 1000;
            while (ports.Running(process) && ports.Milliseconds() < deadline) await ports.Frame(token);
            token.ThrowIfCancellationRequested();
            if (ports.Running(process)) throw new IOException("Replay exceeded its deadline.");
            if (!File.Exists(request.ResultPath)) throw new IOException("Replay exited without a result file.");
            using var json = JsonDocument.Parse(File.ReadAllText(request.ResultPath));
            token.ThrowIfCancellationRequested();
            string outcome = json.RootElement.GetProperty("Result").GetString() ?? throw new InvalidDataException("Missing replay result.");
            completed = true;
            return new(outcome);
        }
        finally
        {
            bool childCleanupFailed = false;
            try
            {
                if (process >= 0 && ports.Running(process))
                {
                    ports.Stop(process);
                    ulong stopDeadline = ports.Milliseconds() + 5000;
                    while (ports.Running(process) && ports.Milliseconds() < stopDeadline) await ports.Frame(CancellationToken.None);
                    if (ports.Running(process)) throw new BugReportChildCleanupException("Could not stop the owned replay process.");
                }
            }
            catch (BugReportChildCleanupException)
            {
                childCleanupFailed = true;
                throw;
            }
            finally
            {
                try
                {
                    if (!completed && File.Exists(request.ResultPath)) File.Delete(request.ResultPath);
                }
                catch (Exception error) when (childCleanupFailed && error is IOException or UnauthorizedAccessException)
                {
                    // A live child may still hold its result open. Preserve the
                    // stop failure that requires a nonzero host exit.
                }
            }
        }
    }
}
