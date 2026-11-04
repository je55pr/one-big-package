using System.Security.Cryptography;
using OBP.Runtime.Diagnostics;

namespace OBP.Game;

/// <summary>Pure validation of a bookmark's bounded, local recording prefix.
/// This never opens worlds or changes the profile, and never edits the bookmark.</summary>
internal static class ReplayBookmarkResolver
{
    public static string Resolve(string path, MovementReproState state)
    {
        state.Validate();
        var replay = state.Replay ?? throw new InvalidDataException("Bookmark recording reference is missing.");
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string recording = Path.GetFullPath(Path.Combine(directory, replay.FileName));
        // A replay is a local sibling of its bookmark, never a parent or child path.
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(recording), directory, comparison))
            throw new InvalidDataException("Bookmark recording must be beside the bookmark.");
        // Do not follow file-level symlinks to a different recording outside the save directory.
        if ((File.GetAttributes(recording) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Bookmark recording cannot be a symbolic link.");
        using var file = File.OpenRead(recording);
        if (file.Length > PlaytestRunReader.MaximumCompressedBytes ||
            !Convert.ToHexString(SHA256.HashData(file)).Equals(replay.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Bookmark recording is missing, too large or has changed.");
        return recording;
    }
}
