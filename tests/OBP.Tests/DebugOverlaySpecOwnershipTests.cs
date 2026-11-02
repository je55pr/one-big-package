namespace OBP.Tests;

public sealed class DebugOverlaySpecOwnershipTests
{
    [Fact]
    public void RootDelegatesOverlayCommandsToTheExistingWorldOverlayOwner()
    {
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.cs"));
        int start = root.IndexOf("private void ApplyOverlaySpec(string? spec)", StringComparison.Ordinal);
        int end = root.IndexOf("private void SpawnPlayer(RuntimeWorld world)", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        string glue = root[start..end];
        Assert.Contains("_overlay?.ApplySpec(spec, message => GD.PrintErr(message))", glue, StringComparison.Ordinal);
        Assert.DoesNotContain("TryIsolateKind", glue, StringComparison.Ordinal);
        Assert.DoesNotContain("TryParse<DebugOverlay.Layer>", glue, StringComparison.Ordinal);

        string owner = File.ReadAllText(Path.Combine(RepoPaths.Root, "src/OBP.Godot/DebugOverlay.cs"));
        int parser = owner.IndexOf("public void ApplySpec(string? spec, Action<string> reportError)", StringComparison.Ordinal);
        int clear = owner.IndexOf("public void Clear()", parser, StringComparison.Ordinal);
        Assert.True(parser >= 0 && clear > parser);
        string policy = owner[parser..clear];
        Assert.True(policy.IndexOf("Clear();", StringComparison.Ordinal) < policy.IndexOf("foreach (string token", StringComparison.Ordinal));
        Assert.Contains("TryIsolateKind(want)", policy, StringComparison.Ordinal);
        Assert.Contains("System.Enum.TryParse<Layer>", policy, StringComparison.Ordinal);
        Assert.Contains("Set(layer, true)", policy, StringComparison.Ordinal);
        Assert.Contains("unknown --overlay isolate kind", policy, StringComparison.Ordinal);
        Assert.Contains("unknown --overlay token", policy, StringComparison.Ordinal);
    }
}
