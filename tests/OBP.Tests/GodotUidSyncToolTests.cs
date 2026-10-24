using System.Text;
using OBP.GodotUidTool;

namespace OBP.Tests;

public sealed class GodotUidSyncToolTests
{
    [Fact]
    public void ExistingUniqueSidecarsCheckAndSyncWithoutGodotOrUidChurn()
    {
        using var repo = new Fixture();
        repo.Add("game/scripts/A.cs", "class A {}");
        string first = repo.Add("game/scripts/A.cs.uid", "uid://cafefeed\n\n");
        repo.Add("game/diagnostics/B.cs", "class B {}");
        repo.Add("game/diagnostics/B.cs.uid", "uid://abcdef123\n");
        repo.Stage();
        byte[] before = File.ReadAllBytes(first);

        Assert.Empty(GodotUidScanner.Scan(repo.Root).Errors);
        Assert.Equal(0, repo.Run("--check").Exit);
        // No Godot toolchain manifest/binary exists in this fixture. A correct
        // no-op sync must not even attempt to discover an editor or build C#.
        Assert.Equal(0, repo.Run("--sync").Exit);
        Assert.Equal(before, File.ReadAllBytes(first));
    }

    [Fact]
    public void MissingUidFailsCheckWithActionablePath()
    {
        using var repo = new Fixture();
        repo.Add("game/scripts/New.cs", "class New {}");

        Assert.Single(GodotUidScanner.Scan(repo.Root).Missing);
        var result = repo.Run("--check");
        Assert.Equal(1, result.Exit);
        Assert.Contains("Missing UID game/scripts/New.cs.uid", result.Error);
        Assert.Contains("sync-godot-uids", result.Error);
    }

    [Fact]
    public void UntrackedSidecarFailsCheckUntilStaged()
    {
        using var repo = new Fixture();
        repo.Add("game/scripts/A.cs", "class A {}");
        repo.Add("game/scripts/A.cs.uid", "uid://abc123\n");

        var failed = repo.Run("--check");
        Assert.Equal(1, failed.Exit);
        Assert.Contains("Untracked UID game/scripts/A.cs.uid", failed.Error);

        repo.Stage();
        Assert.Equal(0, repo.Run("--check").Exit);
    }

    [Fact]
    public void DuplicateMalformedAndOrphanSidecarsAreDiagnosedAndNotChanged()
    {
        using var repo = new Fixture();
        repo.Add("game/scripts/A.cs", "class A {}");
        string original = repo.Add("game/scripts/A.cs.uid", "uid://abc");
        repo.Add("game/diagnostics/B.cs", "class B {}");
        repo.Add("game/diagnostics/B.cs.uid", "uid://abc");
        repo.Add("game/scripts/C.cs", "class C {}");
        repo.Add("game/scripts/C.cs.uid", "UID://INVALID");
        repo.Add("game/diagnostics/Deleted.cs.uid", "uid://deleted");

        var scanned = GodotUidScanner.Scan(repo.Root);
        Assert.Contains(scanned.Errors, message => message.Contains("Duplicate UID") && message.Contains("A.cs.uid") && message.Contains("B.cs.uid"));
        Assert.Contains(scanned.Errors, message => message.Contains("Malformed UID") && message.Contains("C.cs.uid"));
        Assert.Contains(scanned.Errors, message => message.Contains("Orphan UID") && message.Contains("Deleted.cs.uid"));
        byte[] before = File.ReadAllBytes(original);

        var result = repo.Run("--sync");
        Assert.Equal(1, result.Exit);
        Assert.Contains("refused", result.Error);
        Assert.Equal(before, File.ReadAllBytes(original));
    }

    [Fact]
    public void MovingSourceTogetherWithSidecarKeepsTheExactIdentity()
    {
        using var repo = new Fixture();
        string originalSource = repo.Add("game/scripts/Old.cs", "class Old {}");
        string originalSidecar = repo.Add("game/scripts/Old.cs.uid", "uid://permanent123\n\n");
        byte[] before = File.ReadAllBytes(originalSidecar);

        File.Move(originalSource, Path.Combine(repo.Root, "game/scripts/New.cs"));
        string moved = Path.Combine(repo.Root, "game/scripts/New.cs.uid");
        File.Move(originalSidecar, moved);
        repo.Stage();

        Assert.Empty(GodotUidScanner.Scan(repo.Root).Errors);
        Assert.Empty(GodotUidScanner.Scan(repo.Root).Missing);
        Assert.Equal(before, File.ReadAllBytes(moved));
        Assert.Equal(0, repo.Run("--check").Exit);
    }

    [Fact]
    public void RecursivelyFindsScriptsAndOrphansInNestedDirectories()
    {
        using var repo = new Fixture();
        repo.Add("game/scripts/sub/New.cs", "class New {}");
        repo.Add("game/diagnostics/sub/Orphan.cs.uid", "uid://unused");
        var result = GodotUidScanner.Scan(repo.Root);

        Assert.Single(result.Sources);
        Assert.Single(result.Missing);
        Assert.Contains("sub/New.cs.uid", result.Missing[0].Replace('\\', '/'));
        Assert.Contains(result.Errors, error => error.Contains("sub/Orphan.cs.uid"));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "obp-uid-xunit-" + Guid.NewGuid().ToString("N"));

        public Fixture()
        {
            Directory.CreateDirectory(Path.Combine(Root, "game/scripts"));
            Directory.CreateDirectory(Path.Combine(Root, "game/diagnostics"));
            GodotUidScanner.RunInteractive("git", ["init", "-q"], Root);
        }

        public string Add(string relative, string content)
        {
            string full = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content, new UTF8Encoding(false));
            return full;
        }

        public void Stage() => GodotUidScanner.RunInteractive("git", ["-c", "core.autocrlf=false", "add", "game"], Root);

        public (int Exit, string Output, string Error) Run(params string[] args)
        {
            using StringWriter output = new();
            using StringWriter error = new();
            int code = GodotUidCommand.Run(["--root", Root, .. args], output, error);
            return (code, output.ToString(), error.ToString());
        }

        public void Dispose()
        {
            // Git stores read-only objects on Windows; normalize fixture file
            // attributes before removing the throwaway repository.
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }
}
