using System.Text.Json;
using System.Text.Json.Serialization;
using OBP.Core;
using OBP.IO;

namespace OBP.PS2;

/// <summary>
/// Host-neutral registry for locally attached retail sources. It identifies a
/// PS2 disc with bounded ISO-9660/SYSTEM.CNF/ELF reads, matches the boot serial
/// and authority payload size to a caller-supplied set of supported builds, and
/// remembers at most one source per source game.
///
/// This class never copies payload bytes and never performs the multi-gigabyte
/// SHA-256 pass implicitly. Exact verification remains an explicit operation.
/// </summary>
public sealed class ObpSourceLibrary
{
    private const int ConfigVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IReadOnlyList<ObpSourceDefinition> _definitions;
    private readonly Dictionary<ObpSourceGame, ObpSourceDefinition> _byGame;
    private readonly Dictionary<string, ObpSourceDefinition> _bySerial;
    private readonly Dictionary<ObpSourceGame, ObpAttachedSource> _attached = new();

    public ObpSourceLibrary(IEnumerable<ObpSourceDefinition> definitions)
    {
        var defs = definitions.ToArray();
        if (defs.Length == 0)
        {
            throw new ArgumentException("At least one OBP source definition is required.", nameof(definitions));
        }

        _byGame = new Dictionary<ObpSourceGame, ObpSourceDefinition>();
        _bySerial = new Dictionary<string, ObpSourceDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in defs)
        {
            if (string.IsNullOrWhiteSpace(def.DisplayName))
            {
                throw new ArgumentException("OBP source definitions require a display name.", nameof(definitions));
            }

            if (def.ExpectedSizeBytes <= 0)
            {
                throw new ArgumentException($"{def.DisplayName} has invalid expected size {def.ExpectedSizeBytes}.", nameof(definitions));
            }

            string serial = def.Authority.Serial
                ?? throw new ArgumentException($"{def.DisplayName} authority has no PS2 serial.", nameof(definitions));

            if (!_byGame.TryAdd(def.Game, def))
            {
                throw new ArgumentException($"Duplicate source definition for {def.Game}.", nameof(definitions));
            }

            if (!_bySerial.TryAdd(serial, def))
            {
                throw new ArgumentException($"Duplicate source serial {serial}.", nameof(definitions));
            }
        }

        _definitions = Array.AsReadOnly(defs);
    }

    public IReadOnlyList<ObpSourceDefinition> Definitions => _definitions;
    public IReadOnlyDictionary<ObpSourceGame, ObpAttachedSource> Attached => _attached;

    public ObpSourceDefinition Definition(ObpSourceGame game) =>
        _byGame.TryGetValue(game, out var def)
            ? def
            : throw new KeyNotFoundException($"No source definition is registered for {game}.");

    public ObpAttachedSource? Get(ObpSourceGame game) =>
        _attached.TryGetValue(game, out var source) ? source : null;

    /// <summary>
    /// Cheap title/build compatibility probe. A known serial with the wrong
    /// payload size is recognized but not admitted as a supported source; this
    /// prevents a same-serial retail revision from silently masquerading as the
    /// pinned authority build.
    /// </summary>
    public ObpSourceProbe Probe(IRandomAccessReader source)
    {
        try
        {
            var boot = Ps2Boot.ReadBootInfo(source);
            if (boot.Serial is null)
            {
                return new ObpSourceProbe(false, false, null, null, source.Length, false,
                    "No normalized PlayStation 2 boot serial was found.");
            }

            if (!_bySerial.TryGetValue(boot.Serial, out var def))
            {
                return new ObpSourceProbe(false, false, null, boot.Serial, source.Length, false,
                    $"PS2 disc serial {boot.Serial} is not one of the configured OBP source builds.");
            }

            bool sizeMatches = source.Length == def.ExpectedSizeBytes;
            string? problem = sizeMatches
                ? null
                : $"{def.DisplayName} ({boot.Serial}) was recognized, but the payload size is {source.Length:N0} bytes; " +
                  $"the supported {def.Authority.BuildId} authority is {def.ExpectedSizeBytes:N0} bytes.";

            return new ObpSourceProbe(true, sizeMatches, def, boot.Serial, source.Length, sizeMatches, problem);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or NotSupportedException)
        {
            return new ObpSourceProbe(false, false, null, null, source.Length, false,
                $"Could not identify a PS2 disc image: {ex.Message}");
        }
    }

    /// <summary>Probe and attach a supported source, replacing the previous source for that game.</summary>
    public ObpAttachedSource Attach(string path, IRandomAccessReader source)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Attached source path is empty.", nameof(path));
        }

        var probe = Probe(source);
        if (!probe.Supported || probe.Definition is null || probe.DiscSerial is null)
        {
            throw new InvalidDataException(probe.Problem ?? "Unsupported OBP source.");
        }

        var attached = new ObpAttachedSource(path, probe.Definition, probe.DiscSerial, probe.SizeBytes);
        _attached[attached.Game] = attached;
        return attached;
    }

    /// <summary>
    /// Re-probe a persisted path before restoring it. The saved game/build id
    /// must still describe the bytes currently at that path; persisted metadata
    /// is never trusted as proof of identity.
    /// </summary>
    public ObpAttachedSource Restore(ObpSavedSource saved, IRandomAccessReader source)
    {
        var probe = Probe(source);
        if (!probe.Supported || probe.Definition is null || probe.DiscSerial is null)
        {
            throw new InvalidDataException(probe.Problem ?? $"Saved source {saved.Path} is no longer supported.");
        }

        if (probe.Definition.Game != saved.Game ||
            !string.Equals(probe.Definition.Authority.BuildId, saved.BuildId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Saved source {saved.Path} expected {saved.Game}/{saved.BuildId}, but now identifies as " +
                $"{probe.Definition.Game}/{probe.Definition.Authority.BuildId}.");
        }

        var attached = new ObpAttachedSource(saved.Path, probe.Definition, probe.DiscSerial, probe.SizeBytes);
        _attached[attached.Game] = attached;
        return attached;
    }

    public bool Remove(ObpSourceGame game) => _attached.Remove(game);

    public void SaveConfig(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var config = new ConfigFile(
            ConfigVersion,
            _attached.Values
                .OrderBy(s => s.Game)
                .Select(s => new ObpSavedSource(s.Game, s.Identity.BuildId, s.Path))
                .ToArray());
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
    }

    public static IReadOnlyList<ObpSavedSource> LoadConfig(string path)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<ObpSavedSource>();
        }

        var config = JsonSerializer.Deserialize<ConfigFile>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"Source-library config {path} is empty or invalid.");
        if (config.Version != ConfigVersion)
        {
            throw new InvalidDataException(
                $"Unsupported source-library config version {config.Version}; expected {ConfigVersion}.");
        }

        var duplicate = config.Sources.GroupBy(s => s.Game).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"Source-library config contains duplicate entries for {duplicate.Key}.");
        }

        return config.Sources;
    }

    private sealed record ConfigFile(int Version, ObpSavedSource[] Sources);
}
