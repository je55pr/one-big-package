namespace OBP.IO;

public enum DocumentReadKind { Missing, Primary, Backup, Rejected }
public sealed record DocumentRead<T>(T? Value, DocumentReadKind Kind, string? Problem = null) where T : class;

/// <summary>Bounded, same-directory atomic documents with one known-good backup. The caller owns schema and recovery policy.</summary>
public static class AtomicDocumentFile
{
    public static byte[] Read(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximumBytes) throw new InvalidDataException("Document exceeds its size limit.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    public static DocumentRead<T> Load<T>(string path, int maximumBytes, Func<byte[], T> decode, Func<Exception, bool> recoverable) where T : class
    {
        Exception? primary = null;
        try { return new(decode(Read(path, maximumBytes)), DocumentReadKind.Primary); }
        catch (Exception error) when (recoverable(error)) { primary = error; }
        try { return new(decode(Read(path + ".bak", maximumBytes)), DocumentReadKind.Backup, "Recovered the last valid backup; the original document is retained."); }
        catch (Exception error) when (recoverable(error))
        {
            bool missing = (primary is FileNotFoundException or DirectoryNotFoundException) && (error is FileNotFoundException or DirectoryNotFoundException);
            return new(null, missing ? DocumentReadKind.Missing : DocumentReadKind.Rejected,
                missing ? null : "Document and backup could not be loaded; automatic replacement is disabled.");
        }
    }

    public static void Save(string path, byte[] bytes, int maximumBytes, Func<byte[], bool> validExisting)
    {
        if (bytes.Length > maximumBytes) throw new InvalidDataException("Document exceeds its size limit.");
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temporary = full + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            bool exists = File.Exists(full);
            bool valid = false;
            if (exists)
            {
                try { valid = validExisting(Read(full, maximumBytes)); }
                catch (InvalidDataException) { /* An oversized primary is retained as rejected data below. */ }
            }
            if (exists && valid) File.Replace(temporary, full, full + ".bak");
            else if (exists) File.Replace(temporary, full, full + ".rejected-" + Guid.NewGuid().ToString("N"));
            else File.Move(temporary, full);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
