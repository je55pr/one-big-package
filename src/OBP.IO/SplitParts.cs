using System.Text.RegularExpressions;

namespace OBP.IO;

/// <summary>
/// Orders numbered raw-split files (<c>name.iso.001</c>, <c>name.iso.002</c>, …)
/// and joins them into one <see cref="ConcatenatedRandomAccessReader"/>. Mirrors
/// the TypeScript <c>orderNumberedSplitParts</c> / <c>createNumberedSplitFileReader</c>.
/// </summary>
public static partial class SplitParts
{
    [GeneratedRegex(@"^(.*)\.([0-9]{3,})$")]
    private static partial Regex NumberedSuffix();

    /// <summary>Sort by numeric suffix and verify one shared stem and a contiguous 1..N run.</summary>
    public static IReadOnlyList<string> Order(IReadOnlyList<string> fileNames)
    {
        if (fileNames.Count == 0)
        {
            throw new ArgumentException("At least one numbered split part is required.", nameof(fileNames));
        }

        var parsed = fileNames.Select(name =>
        {
            var m = NumberedSuffix().Match(Path.GetFileName(name));
            if (!m.Success)
            {
                throw new ArgumentException($"Split part '{name}' does not end in a numeric suffix such as .001.");
            }

            int index = int.Parse(m.Groups[2].Value);
            if (index <= 0)
            {
                throw new ArgumentException($"Split part '{name}' has an invalid part number.");
            }

            return (name, index, stem: m.Groups[1].Value);
        }).OrderBy(p => p.index).ToList();

        string stem = parsed[0].stem;
        for (int i = 0; i < parsed.Count; i++)
        {
            if (parsed[i].stem != stem)
            {
                throw new ArgumentException($"Split parts do not share one filename stem: '{stem}' vs '{parsed[i].stem}'.");
            }

            if (parsed[i].index != i + 1)
            {
                throw new ArgumentException($"Split part sequence is not contiguous: expected part {i + 1}, got {parsed[i].index}.");
            }
        }

        return parsed.Select(p => p.name).ToList();
    }

    /// <summary>Open ordered split files on disk as one logical seekable source.</summary>
    public static ConcatenatedRandomAccessReader OpenFiles(IReadOnlyList<string> paths, string? logicalName = null)
    {
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in paths)
        {
            byName[Path.GetFileName(p)] = p;
        }

        var ordered = Order(byName.Keys.ToList());
        var readers = ordered.Select(name => (IRandomAccessReader)new FileRandomAccessReader(byName[name], name)).ToList();
        string stem = NumberedSuffix().Match(ordered[0]).Groups[1].Value;
        return new ConcatenatedRandomAccessReader(readers, logicalName ?? stem);
    }
}
