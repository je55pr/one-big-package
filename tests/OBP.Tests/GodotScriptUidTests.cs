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
            .SelectMany(folder => Directory.GetFiles(Path.Combine(game, folder), "*.cs", SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(sources);

        var uids = new HashSet<string>(StringComparer.Ordinal);
        foreach (string source in sources)
        {
            string sidecar = source + ".uid";
            Assert.True(File.Exists(sidecar),
                $"Missing Godot UID sidecar: {Path.GetRelativePath(RepoPaths.Root, sidecar)}. Run ./tools/sync-godot-uids.ps1 (or bash tools/sync-godot-uids.sh) and commit the generated file.");

            string uid = File.ReadAllText(sidecar).Trim();
            Assert.True(System.Text.RegularExpressions.Regex.IsMatch(uid, "^uid://[a-z0-9]+$"),
                $"Malformed Godot UID in {Path.GetRelativePath(RepoPaths.Root, sidecar)}: '{uid}'. Restore its original value; see tools/sync-godot-uids.mjs --check.");
            Assert.True(uids.Add(uid),
                $"Duplicate Godot script UID {uid}: {Path.GetRelativePath(RepoPaths.Root, sidecar)}. Restore the original sidecar; see tools/sync-godot-uids.mjs --check.");
        }

        foreach (string folder in new[] { "scripts", "diagnostics" })
        {
            foreach (string sidecar in Directory.GetFiles(Path.Combine(game, folder), "*.cs.uid", SearchOption.AllDirectories))
                Assert.True(File.Exists(sidecar[..^4]),
                    $"Orphan Godot UID sidecar: {sidecar}. Move the .cs.uid with its renamed script or remove the obsolete sidecar.");
        }
    }
}
