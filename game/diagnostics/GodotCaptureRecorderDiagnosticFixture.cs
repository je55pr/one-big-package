namespace OBP.Game.Diagnostics;

internal sealed class GodotCaptureRecorderDiagnosticFixture(Func<CaptureRuntimeDiagnostic> read, Action<int, int> resize,
    Func<string?> recordingPath, Action<string> bookmark, Action save) : ICaptureRecorderDiagnosticFixture
{
    public CaptureRuntimeDiagnostic Read() => read();
    public void ResizeWindow(int width, int height) => resize(width, height);
    public ICaptureBookmarkDiagnostic RequestBookmark(int visit, string destination)
    {
        string file = recordingPath() ?? throw new DiagnosticUnavailableException("The capture recording path is unavailable.");
        string path = Path.Combine(Path.GetFullPath(file) + ".bookmarks", $"{visit:D2}-" + destination.Replace(':', '-') + ".bookmark.json");
        bookmark(path); return new Bookmark(path);
    }
    public void Save() => save();
    private sealed class Bookmark(string path) : ICaptureBookmarkDiagnostic
    {
        public bool Exists => File.Exists(path);
        public string? ReadDestination() => MovementReproStateFile.Load(path).Destination;
    }
}
