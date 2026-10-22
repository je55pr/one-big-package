namespace OBP.Tests;

/// <summary>
/// Godot assigns persistent script identities in .cs.uid sidecars; a new C# source
/// must check in its generated sidecar rather than dirtying a clean import.
/// </summary>
public sealed class GodotScriptUidTests
{
    [Fact]
    public void EveryGameCSharpScriptHasUniquePersistentUid()
    {
        var game = Path.Combine(RepoPaths.Root, "game");
        var sources = new[] { "scripts", "diagnostics" }
            .SelectMany(folder => Directory.GetFiles(Path.Combine(game, folder), "*.cs"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(sources);

        var uids = new HashSet<string>(StringComparer.Ordinal);
        foreach (string source in sources)
        {
            string sidecar = source + ".uid";
            Assert.True(File.Exists(sidecar),
                $"Missing Godot UID sidecar: {Path.GetRelativePath(RepoPaths.Root, sidecar)}");

            string uid = File.ReadAllText(sidecar).Trim();
            Assert.Matches("^uid://[a-z0-9]+$", uid);
            Assert.True(uids.Add(uid),
                $"Duplicate Godot script UID {uid}: {Path.GetRelativePath(RepoPaths.Root, sidecar)}");
        }

        foreach (string folder in new[] { "scripts", "diagnostics" })
        {
            foreach (string sidecar in Directory.GetFiles(Path.Combine(game, folder), "*.cs.uid"))
                Assert.True(File.Exists(sidecar[..^4]), $"Orphan Godot UID sidecar: {sidecar}");
        }
    }
}
