using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OBP.Fusion;

namespace OBP.Game;

internal sealed partial class PlaytestDriver
{
    private string? _bookmarkPath;
    private bool _bookmarkSaving;

    public void RequestBookmark(string path)
    {
        if (Reader is not null || _finished || WaitingForEntry || PreparingWorld || !Ports.OrdinaryPlayer() || _bookmarkSaving)
        { Ports.MovementNotice("A universal bookmark needs an active ordinary recording and a ready world."); return; }
        _bookmarkPath = path;
    }

    private async Task SaveBookmarkAsync(string path, int step)
    {
        _bookmarkSaving = true;
        string? recording = null;
        try
        {
            Session.ValidateRecordingSources();
            var state = Ports.Bookmark();
            string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(directory);
            MovementReplayReference? previous = null;
            try { if (File.Exists(path)) previous = MovementReproStateFile.Load(path).Replay; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException) { /* Preserve an unreadable predecessor. */ }
            recording = Path.Combine(directory, "bookmark-" + Guid.NewGuid().ToString("N") + ".run.jsonl.gz");
            await _writer!.ExportSnapshotAsync(recording);
            Session.ValidateRecordingSources();
            using (var stream = File.OpenRead(recording))
                state = state with { Replay = new(Path.GetFileName(recording), Convert.ToHexString(SHA256.HashData(stream)), step) };
            MovementReproStateFile.Save(path, state);
            recording = null;
            if (previous is not null) await Task.Run(() => MovementReproStateFile.RetireSupersededPrefix(path, previous));
            if (!_finalized && !Ports.Operations.Stopping)
                Ports.MovementNotice("Universal bookmark saved with a completed replay prefix. Move both sibling files together.");
            GD.Print("[movement-state] universal bookmark saved at step " + step);
        }
        catch (Exception error)
        {
            if (_finalized || Ports.Operations.Stopping) throw;
            Ports.MovementNotice("Bookmark not saved: " + error.Message);
        }
        finally
        {
            _bookmarkSaving = false;
            try { if (recording is not null) File.Delete(recording); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Incomplete prefix remains inspectable. */ }
        }
    }
}
