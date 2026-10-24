using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace OBP.Runtime.Diagnostics;

internal static class PlaytestRunLimits
{
    internal const int Line = 4 * 1024 * 1024;
    internal const long Expanded = 512L * 1024 * 1024;
    internal const long Compressed = 128L * 1024 * 1024;
}

/// <summary>Bounded background writer. Only a completed, flushed run takes the final filename.</summary>
public sealed class PlaytestRunWriter : IDisposable
{
    private sealed record WriteRequest(PlaytestRunEntry? Entry = null, string? Snapshot = null,
        int Steps = 0, TaskCompletionSource? Completion = null);
    private readonly BlockingCollection<WriteRequest> _queue = new(256);
    private readonly Task _worker;
    private int _steps;
    private bool _finished;
    public string Path { get; }
    public string PartialPath => Path + ".partial";

    public PlaytestRunWriter(string path, PlaytestRunHeader header)
    {
        header.Validate();
        Path = System.IO.Path.GetFullPath(path);
        if (File.Exists(Path) || File.Exists(PartialPath)) throw new IOException("Choose a new recording filename.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var file = new FileStream(PartialPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, FileOptions.SequentialScan);
        _worker = Task.Run(() =>
        {
            bool completed = false;
            long expanded = 0;
            using (file)
            {
                var text = OpenText(file);
                try
                {
                    foreach (var request in _queue.GetConsumingEnumerable())
                    {
                        if (request.Snapshot is { } snapshot)
                        {
                            // Close this gzip member before copying. The live tape
                            // gets another member; only the exported copy gets an
                            // end record. No input records are rewritten or lost.
                            text.Dispose();
                            try
                            {
                                file.Flush(flushToDisk: true);
                                ExportSnapshot(snapshot, request.Steps);
                                request.Completion!.SetResult();
                            }
                            catch (Exception error) { request.Completion!.SetException(error); }
                            finally { text = OpenText(file); }
                            continue;
                        }
                        var entry = request.Entry!;
                        string line = JsonSerializer.Serialize(entry, PlaytestRunEntry.JsonOptions);
                        expanded += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
                        if (line.Length > PlaytestRunLimits.Line || expanded > PlaytestRunLimits.Expanded || file.Position > PlaytestRunLimits.Compressed)
                            throw new IOException("Recording exceeded its size limit; partial file retained.");
                        text.WriteLine(line);
                        completed |= entry.End is not null;
                    }
                }
                finally { text.Dispose(); }
            }
            if (new FileInfo(PartialPath).Length > PlaytestRunLimits.Compressed)
                throw new IOException("Recording exceeded its compressed size limit; partial file retained.");
            if (completed) File.Move(PartialPath, Path);
        });
        Enqueue(new(Header: header));
    }

    private static StreamWriter OpenText(Stream stream) => new(
        new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true), new UTF8Encoding(false), 65536);

    /// <summary>
    /// Export the complete prefix queued so far, without stopping the live writer.
    /// A failed export does not complete or invalidate the ongoing recording.
    /// </summary>
    public async Task ExportSnapshotAsync(string path)
    {
        string target = System.IO.Path.GetFullPath(path);
        if (_steps == 0 || string.Equals(target, Path, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(target, PartialPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a separate snapshot filename after recording begins.", nameof(path));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EnqueueRequest(new(Snapshot: target, Steps: _steps, Completion: completion));
        if (await Task.WhenAny(completion.Task, _worker).ConfigureAwait(false) == _worker)
            await _worker.ConfigureAwait(false);
        await completion.Task.ConfigureAwait(false);
    }

    private void ExportSnapshot(string target, int steps)
    {
        string partial = target + ".partial";
        // CreateNew prevents collisions with another report/export operation.
        bool created = false;
        try
        {
            if (File.Exists(target)) throw new IOException("Snapshot already exists.");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                using (var input = new FileStream(PartialPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    input.CopyTo(output);
                using (var footer = OpenText(output))
                    footer.WriteLine(JsonSerializer.Serialize(new PlaytestRunEntry(End: new(steps, "saved")), PlaytestRunEntry.JsonOptions));
                output.Flush(flushToDisk: true);
                if (output.Length > PlaytestRunLimits.Compressed) throw new IOException("Snapshot exceeds its compressed size limit.");
            }
            File.Move(partial, target);
        }
        finally { if (created && File.Exists(partial)) File.Delete(partial); }
    }

    public void Add(PlaytestStep step)
    {
        step.Validate();
        if (step.Index != _steps) throw new InvalidDataException("Recorded step sequence is not contiguous.");
        Enqueue(new(Step: step));
        _steps++;
    }

    public void Mark(PlaytestMarker marker)
    {
        if (marker.Step < 0 || marker.Step >= _steps || string.IsNullOrWhiteSpace(marker.Label) || marker.Label.Length > 256)
            throw new InvalidDataException("Invalid playtest marker.");
        Enqueue(new(Marker: marker));
    }

    public Task FinishAsync(string reason)
    {
        if (_finished) return _worker;
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 256) throw new ArgumentException("Invalid recording end reason.", nameof(reason));
        Enqueue(new(End: new(_steps, reason)));
        _finished = true;
        _queue.CompleteAdding();
        return _worker;
    }

    private void Enqueue(PlaytestRunEntry entry) => EnqueueRequest(new(Entry: entry));

    private void EnqueueRequest(WriteRequest request)
    {
        if (_finished) throw new InvalidOperationException("Recording is already closed.");
        if (_worker.IsFaulted) throw new IOException("Recording writer failed.", _worker.Exception);
        if (!_queue.TryAdd(request)) throw new IOException("Recording writer fell behind; the partial file is retained.");
    }

    public void Dispose()
    {
        if (!_finished)
        {
            _finished = true;
            _queue.CompleteAdding();
        }
        try { _worker.GetAwaiter().GetResult(); }
        finally { _queue.Dispose(); }
    }
}

/// <summary>Streaming reader with explicit limits on decompressed data and individual records.</summary>
public sealed class PlaytestRunReader : IDisposable
{
    private readonly StreamReader _reader;
    private long _expanded;
    private int _steps;
    private ulong _lastMilliseconds;
    private bool _ended;
    public PlaytestRunHeader Header { get; }
    public PlaytestRunEnd? End { get; private set; }
    public List<PlaytestMarker> Markers { get; } = [];

    public PlaytestRunReader(string path)
    {
        var file = File.OpenRead(path);
        if (file.Length > PlaytestRunLimits.Compressed) { file.Dispose(); throw new InvalidDataException("Recording exceeds the compressed size limit."); }
        _reader = new(new GZipStream(file, CompressionMode.Decompress), new UTF8Encoding(false, true), false, 65536);
        try
        {
            var first = ReadEntry();
            Header = first.Header ?? throw new InvalidDataException("Recording must start with its header.");
            Header.Validate();
        }
        catch { _reader.Dispose(); throw; }
    }

    public PlaytestStep? ReadNextStep()
    {
        if (_ended) return null;
        while (true)
        {
            var entry = ReadEntry();
            if (entry.Step is { } step)
            {
                step.Validate();
                if (step.Index != _steps++ || step.Milliseconds < _lastMilliseconds)
                    throw new InvalidDataException("Recording step order or clock is invalid.");
                foreach (var input in step.Events)
                {
                    if (input.Milliseconds < _lastMilliseconds || input.Milliseconds > step.Milliseconds)
                        throw new InvalidDataException("Recorded event clock is out of order.");
                    _lastMilliseconds = input.Milliseconds;
                }
                _lastMilliseconds = step.Milliseconds;
                return step;
            }
            if (entry.Marker is { } marker)
            {
                if (Markers.Count >= 4096 || marker.Step < 0 || marker.Step >= _steps ||
                    string.IsNullOrWhiteSpace(marker.Label) || marker.Label.Length > 256)
                    throw new InvalidDataException("Invalid recorded marker.");
                Markers.Add(marker);
                continue;
            }
            End = entry.End ?? throw new InvalidDataException("Unexpected recording header.");
            if (End.Steps != _steps || _steps == 0 || string.IsNullOrWhiteSpace(End.Reason) || End.Reason.Length > 256)
                throw new InvalidDataException("Invalid recording end record.");
            if (_reader.Read() != -1) throw new InvalidDataException("Data follows the recording end record.");
            _ended = true;
            return null;
        }
    }

    private PlaytestRunEntry ReadEntry()
    {
        var line = new StringBuilder();
        int character, read = 0, carriageReturns = 0;
        while ((character = _reader.Read()) != -1 && character != '\n')
        {
            if (++read > PlaytestRunLimits.Line)
                throw new InvalidDataException("Recording exceeds its decompressed limits.");
            if (character != '\r') line.Append((char)character); else carriageReturns++;
        }
        if (character == -1) throw new InvalidDataException("Recording is incomplete; no complete end record.");
        string json = line.ToString();
        _expanded += Encoding.UTF8.GetByteCount(json) + carriageReturns + 1;
        if (_expanded > PlaytestRunLimits.Expanded) throw new InvalidDataException("Recording exceeds its decompressed limits.");
        var entry = JsonSerializer.Deserialize<PlaytestRunEntry>(json, PlaytestRunEntry.JsonOptions)
            ?? throw new InvalidDataException("Empty recording entry.");
        if ((entry.Header is null ? 0 : 1) + (entry.Step is null ? 0 : 1) +
            (entry.Marker is null ? 0 : 1) + (entry.End is null ? 0 : 1) != 1)
            throw new InvalidDataException("Ambiguous recording entry.");
        return entry;
    }

    public void Dispose() => _reader.Dispose();
}
