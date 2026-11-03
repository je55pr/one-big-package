using Godot;
using OBP.Core;
using OBP.Core.Math;
using OBP.Game;
using OBP.Godot;
using OBP.Runtime;

namespace OBP.Tests;

public sealed class WorldHudOwnershipTests
{
    [Fact]
    public void RootOnlySuppliesDebugFactsAndDoesNotFormatNativeWorldHudText()
    {
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.cs"));
        string hud = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/WorldHud.cs"));

        Assert.Contains("Presentation.UpdateWorldHud(", root, StringComparison.Ordinal);
        string presentation = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/GodotApplicationPresentation.cs"));
        Assert.Contains("_worldHud = new WorldHud", presentation, StringComparison.Ordinal);
        Assert.Contains("hud.Render(new WorldHudFrame(", presentation, StringComparison.Ordinal);
        Assert.Contains("resources?.Rac1Gameplay?.Hud.Line()", presentation, StringComparison.Ordinal);
        Assert.Contains("resources?.Rac2Gameplay?.HudLine()", presentation, StringComparison.Ordinal);
        Assert.Contains("resources?.Rac3Gameplay?.HudLine()", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("Rac2PlanetCatalogue.Find(", root, StringComparison.Ordinal);
        Assert.DoesNotContain("Game: {gameLabel}", root, StringComparison.Ordinal);
        Assert.DoesNotContain("Render tris: {r.Triangles", root, StringComparison.Ordinal);

        Assert.Contains("Rac2PlanetCatalogue.Find(w.LevelId)", hud, StringComparison.Ordinal);
        Assert.Contains("Game: {gameLabel}", hud, StringComparison.Ordinal);
        Assert.Contains("Render tris: {r.Triangles", hud, StringComparison.Ordinal);
        Assert.Contains("frame.HasAudioDiagnostics", hud, StringComparison.Ordinal);
        Assert.Contains("F1 isolate · F2 tint · F3 collision · F4 bounds · F5 lights · F6 sky · F7 clear", hud, StringComparison.Ordinal);
        Assert.Contains("frame.Rac1Status", hud, StringComparison.Ordinal);
        Assert.Contains("frame.Rac2Status", hud, StringComparison.Ordinal);
        Assert.Contains("frame.Rac3Status", hud, StringComparison.Ordinal);
    }

    [Fact]
    public void NewDebugPresenterCannotOwnAnIndependentGameplayOrPlayerSession()
    {
        string hud = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/WorldHud.cs"));
        Assert.DoesNotContain("new PlayerHost(", hud, StringComparison.Ordinal);
        Assert.DoesNotContain("new Rac1CampaignRuntimeSession(", hud, StringComparison.Ordinal);
        Assert.DoesNotContain("WorldSceneLifetime<", hud, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerAvatarPresentationHost.Provider(", hud, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ObpSourceGame.Rac1, "Ratchet & Clank")]
    [InlineData(ObpSourceGame.Rac2, "Going Commando")]
    [InlineData(ObpSourceGame.Rac3, "Up Your Arsenal")]
    public void PureHudFormatterPreservesSourceIdentityStatsAndStatusOrder(ObpSourceGame game, string expectedLabel)
    {
        var world = new RuntimeWorld(game, "test-build", 9999, "Test Planet", null,
            [], [], 0, [], new ObpBounds(Vec3.Zero, Vec3.Zero), null, null);
        var scene = new RuntimeWorldScene.Result(null!, null, 5, 1234, 0, 0, 567, 8, 9, 10,
            AnimatedMobies: 1, DynamicObjects: 2);
        var frame = new WorldHudFrame(world, scene, null, 3, new Vector3(1, 2, 3), true,
            "overlays: active", true, "audio-detail", "crate-detail", "rac2-detail",
            "rac1-detail", "taxi-detail", "rac3-detail");

        string result = WorldHud.Format(frame);

        Assert.Contains($"Game: {expectedLabel}   Build: test-build", result, StringComparison.Ordinal);
        Assert.Contains("Destination: Test Planet", result, StringComparison.Ordinal);
        Assert.Contains("Level id: 9999", result, StringComparison.Ordinal);
        Assert.Contains("switch #3", result, StringComparison.Ordinal);
        Assert.Contains("Player XYZ:", result, StringComparison.Ordinal);
        Assert.Contains("grounded", result, StringComparison.Ordinal);
        Assert.Contains("Render tris:", result, StringComparison.Ordinal);
        Assert.Contains("Collision tris:", result, StringComparison.Ordinal);
        Assert.Contains("TIE meshes: 8   Shrub meshes: 9   Moby meshes: 10", result, StringComparison.Ordinal);
        Assert.Contains("Animated mobies: 1   Dynamic objects: 2", result, StringComparison.Ordinal);
        Assert.Contains("overlays: active", result, StringComparison.Ordinal);
        string[] statuses = ["audio-detail", "crate-detail", "rac2-detail", "rac1-detail", "taxi-detail", "rac3-detail"];
        int last = -1;
        foreach (string status in statuses)
        {
            int next = result.IndexOf(status, StringComparison.Ordinal);
            Assert.True(next > last);
            last = next;
        }
    }

    [Fact]
    public void WorldHudHidesAndClearsTextForUnavailableFrames()
    {
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.cs"));
        string hud = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/WorldHud.cs"));
        string presentation = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/GodotApplicationPresentation.cs"));
        Assert.Contains("if (entry?.Plan.Prepared.World is not { } world || entry?.Resources.Scene is not { } scene)\n        {\n            hud.Render(null, false);", presentation.Replace("\r\n", "\n"), StringComparison.Ordinal);
        int start = hud.IndexOf("internal void Render(WorldHudFrame? frame, bool enabled)", StringComparison.Ordinal);
        int end = hud.IndexOf("internal static string Format(WorldHudFrame frame)", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        string render = hud[start..end];
        Assert.Contains("Visible = enabled;", render, StringComparison.Ordinal);
        Assert.Contains("if (!enabled)", render, StringComparison.Ordinal);
        Assert.Contains("Text = string.Empty;", render, StringComparison.Ordinal);
        Assert.Contains("if (frame is null) return;", render, StringComparison.Ordinal);
    }
}
