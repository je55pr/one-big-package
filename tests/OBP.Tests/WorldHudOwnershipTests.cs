namespace OBP.Tests;

public sealed class WorldHudOwnershipTests
{
    [Fact]
    public void RootOnlySuppliesDebugFactsAndDoesNotFormatNativeWorldHudText()
    {
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.cs"));
        string hud = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/WorldHud.cs"));

        Assert.Contains("_worldHud = new WorldHud", root, StringComparison.Ordinal);
        Assert.Contains("hud.Render(new WorldHudFrame(", root, StringComparison.Ordinal);
        Assert.Contains("resources?.Rac1Gameplay?.Hud.Line()", root, StringComparison.Ordinal);
        Assert.Contains("resources?.Rac2Gameplay?.HudLine()", root, StringComparison.Ordinal);
        Assert.Contains("resources?.Rac3Gameplay?.HudLine()", root, StringComparison.Ordinal);
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
}
