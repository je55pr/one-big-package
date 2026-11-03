namespace OBP.Fusion;

/// <summary>
/// Source-file identity around reversible world preparation. This checks
/// an unchanged on-disk file before commit but does not grant source authority.
/// </summary>
public static class WorldSourceFileStamp
{
    public static string ChangedMessage { get; } =
        "The world source changed during preparation. Reattach it in Game Sources.";

    public static (long Length, long Written)? Capture(string? path)
    {
        if (path is null) return null;
        var file = new FileInfo(path);
        if (!file.Exists)
            throw new FileNotFoundException("The world source is missing. Reattach it in Game Sources.");
        return (file.Length, file.LastWriteTimeUtc.Ticks);
    }

    public static void RequireUnchanged(string? path, (long Length, long Written)? expected)
    {
        if (Capture(path) != expected) throw new InvalidDataException(ChangedMessage);
    }
}
