using OBP.Core;
using OBP.IO;

namespace OBP.PS2;

public readonly record struct SourceFileIdentity(long Length, long Written, long Created);
public interface ISourceValidationFiles
{
    SourceFileIdentity Inspect(string path);
    IRandomAccessReader Open(string path);
}

/// <summary>Bounded required-authority checks, cached by path and cheap file identity. Never hashes a complete ISO.</summary>
public sealed class SourceReadinessEvaluator
{
    private sealed class PhysicalFiles : ISourceValidationFiles
    {
        public SourceFileIdentity Inspect(string path)
        {
            var file = new FileInfo(path);
            if (!file.Exists) throw new FileNotFoundException("The configured disc image is missing or inaccessible.");
            return new(file.Length, file.LastWriteTimeUtc.Ticks, file.CreationTimeUtc.Ticks);
        }
        public IRandomAccessReader Open(string path) => new FileRandomAccessReader(path);
    }

    private sealed record Cached(string Path, SourceFileIdentity Identity, SourceReadinessEntry Result);
    private readonly ObpSourceLibrary _library;
    private readonly ObpSourceDefinition[] _required;
    private readonly ISourceValidationFiles _files;
    private readonly Dictionary<ObpSourceGame, Cached> _checked = [];

    public SourceReadinessEvaluator(ObpSourceLibrary library, IEnumerable<ObpSourceDefinition> required, ISourceValidationFiles? files = null)
    {
        _library = library;
        _required = required.ToArray();
        if (_required.Length == 0 || _required.Select(definition => definition.Game).Distinct().Count() != _required.Length)
            throw new ArgumentException("Required source authorities must be nonempty and unique.", nameof(required));
        _files = files ?? new PhysicalFiles();
    }

    public SourceReadiness Evaluate() => new(Array.AsReadOnly(_required.Select(Check).ToArray()));

    private SourceReadinessEntry Check(ObpSourceDefinition required)
    {
        SourceReadinessEntry Result(SourceReadinessKind kind, string? problem) => new(required.Game, required.DisplayName, kind, problem);
        var attached = _library.Get(required.Game);
        if (attached is null) return Result(SourceReadinessKind.Missing, "Select the supported NTSC-U disc image.");
        if (attached.Identity != required.Authority || attached.SizeBytes != required.ExpectedSizeBytes ||
            !string.Equals(attached.DiscSerial, required.Authority.Serial, StringComparison.OrdinalIgnoreCase))
            return Result(SourceReadinessKind.Unsupported, "The attached source does not match the required supported authority.");
        try
        {
            var identity = _files.Inspect(attached.Path);
            if (_checked.TryGetValue(required.Game, out var cached) && cached.Path == attached.Path && cached.Identity == identity)
                return cached.Result;
            var reader = _files.Open(attached.Path);
            using var ownedReader = reader as IDisposable;
            var probe = _library.Probe(reader);
            var result = probe.Supported && probe.Definition?.Authority == required.Authority && probe.SizeBytes == required.ExpectedSizeBytes
                ? Result(SourceReadinessKind.Ready, null)
                : Result(SourceReadinessKind.Unsupported, probe.Problem ?? "The configured file identifies as a different supported game/build.");
            // A replacement during the bounded probe cannot inherit the earlier validation.
            if (_files.Inspect(attached.Path) != identity)
                return Result(SourceReadinessKind.Unavailable, "The source changed while it was being identified; select it again.");
            _checked[required.Game] = new(attached.Path, identity, result);
            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            _checked.Remove(required.Game);
            return Result(SourceReadinessKind.Unavailable, error.Message);
        }
    }
}
