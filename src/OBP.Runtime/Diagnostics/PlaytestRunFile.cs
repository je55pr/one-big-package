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
    private readonly BlockingCollection<PlaytestRunEntry> _queue = new(256);
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
            using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
            using (var text = new StreamWriter(gzip, new UTF8Encoding(false), 65536))
                foreach (var entry in _queue.GetConsumingEnumerable())
                {
                    string line = JsonSerializer.Serialize(entry, PlaytestRunEntry.JsonOptions);
                    expanded += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
                    if (line.Length > PlaytestRunLimits.Line || expanded > PlaytestRunLimits.Expanded || file.Position > PlaytestRunLimits.Compressed)
                        throw new IOException("Recording exceeded its size limit; partial file retained.");
                    text.WriteLine(line);
                    completed |= entry.End is not null;
                }
            if (new FileInfo(PartialPath).Length > PlaytestRunLimits.Compressed)
                throw new IOException("Recording exceeded its compressed size limit; partial file retained.");
            if (completed) File.Move(PartialPath, Path);
        });
        Enqueue(new(Header: header));
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

    private void Enqueue(PlaytestRunEntry entry)
    {
        if (_finished) throw new InvalidOperationException("Recording is already closed.");
        if (_worker.IsFaulted) throw new IOException("Recording writer failed.", _worker.Exception);
        if (!_queue.TryAdd(entry)) throw new IOException("Recording writer fell behind; the partial file is retained.");
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
