using OBP.Game;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

public sealed class SmokeSceneFixtureOwnershipTests
{
    [Fact]
    public void RootDelegatesSyntheticSceneGeometryAndHudFixtureToDedicatedOwner()
    {
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.cs"));
        string fixture = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/diagnostics/Startup/GodotSmokeSceneFixture.cs"));
        int start = root.IndexOf("private void BuildScene(string scene)", StringComparison.Ordinal);
        int end = root.IndexOf("private bool Rac1PlanetMapOpen", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);

        string glue = root[start..end];
        Assert.Contains("_mode = Mode.Smoke;", glue, StringComparison.Ordinal);
        Assert.Contains("_sceneKind = scene;", glue, StringComparison.Ordinal);
        Assert.Contains("GodotSmokeSceneFixture.BuildApplicationScene(this, Presentation, _hudState, scene)", glue, StringComparison.Ordinal);
        Assert.Contains("EnsurePlainEnvironment(root)", fixture, StringComparison.Ordinal);
        Assert.Contains("application.EnsurePlayerHud()", fixture, StringComparison.Ordinal);
        Assert.Contains("application.PlayerHud!.Render(hud.BeginSession(SmokeHudFixtureState.Create()))", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("new MeshInstance3D", glue, StringComparison.Ordinal);
        Assert.DoesNotContain("new DirectionalLight3D", glue, StringComparison.Ordinal);
        Assert.DoesNotContain("new HudHealth(", glue, StringComparison.Ordinal);
        var syntax = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(glue).GetRoot();
        Assert.DoesNotContain(
            syntax.DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclarationSyntax>(),
            declaration =>
                declaration.Type.ToString() is "MeshInstance3D" or "DirectionalLight3D" or "HudHealth" &&
                declaration.DescendantNodes()
                    .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ImplicitObjectCreationExpressionSyntax>()
                    .Any());
        Assert.DoesNotContain("BuildHudFixture(", root, StringComparison.Ordinal);

        Assert.Contains("root.AddChild(sun)", fixture, StringComparison.Ordinal);
        Assert.Contains("worldRoot.AddChild(new MeshInstance3D", fixture, StringComparison.Ordinal);
        Assert.Contains("camera.LookAt(new Vector3(0, 0.8f, 0), Vector3.Up)", fixture, StringComparison.Ordinal);
        Assert.Contains("ui.AddChild(new Label", fixture, StringComparison.Ordinal);
    }

    [Fact]
    public void BootstrapEnvironmentAndCleanupAreOwnedByTheSameFixture()
    {
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.cs"));
        string worldEntry = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/World/GodotWorldResources.cs"));
        string fixture = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/diagnostics/Startup/GodotSmokeSceneFixture.cs"));

        Assert.DoesNotContain("private void EnsurePlainEnvironment()", root, StringComparison.Ordinal);
        Assert.DoesNotContain("private void ClearBootstrapSceneArtifacts()", root, StringComparison.Ordinal);
        Assert.DoesNotContain("new WorldEnvironment", root, StringComparison.Ordinal);
        Assert.Equal(3, root.Split("GodotSmokeSceneFixture.EnsurePlainEnvironment(this);", StringSplitOptions.None).Length - 1);

        Assert.Contains("var existing = root.GetNodeOrNull<Node>(\"PlainEnvironment\");", fixture, StringComparison.Ordinal);
        int queuedGuard = fixture.IndexOf("if (!existing.IsQueuedForDeletion())", StringComparison.Ordinal);
        int detach = fixture.IndexOf("root.RemoveChild(existing);", StringComparison.Ordinal);
        int create = fixture.IndexOf("root.AddChild(new WorldEnvironment", StringComparison.Ordinal);
        Assert.True(queuedGuard >= 0 && detach > queuedGuard && create > detach,
            "Same-frame bootstrap reentry must detach queued nodes before reusing the PlainEnvironment name.");
        Assert.Contains("BackgroundColor = new Color(0.04f, 0.05f, 0.08f)", fixture, StringComparison.Ordinal);
        Assert.Contains("AmbientLightEnergy = 1.0f", fixture, StringComparison.Ordinal);

        int retire = worldEntry.IndexOf("GodotSmokeSceneFixture.RetireBootstrap(bindings.Host, application.WorldRoot, application.Ui);", StringComparison.Ordinal);
        int playerBind = worldEntry.IndexOf("PlayerLife = bindings.PlayerLife.BindWorld(", StringComparison.Ordinal);
        Assert.True(retire >= 0 && playerBind > retire);

        int position = fixture.IndexOf("internal static void RetireBootstrap(", StringComparison.Ordinal);
        Assert.True(position >= 0);
        foreach (string node in new[]
        {
            "root.GetNodeOrNull<Node>(\"PlainEnvironment\")?.QueueFree();",
            "root.GetNodeOrNull<Node>(\"Sun\")?.QueueFree();",
            "worldRoot.GetNodeOrNull<Node>(\"Floor\")?.QueueFree();",
            "worldRoot.GetNodeOrNull<Node>(\"SmokeCube\")?.QueueFree();",
            "ui.GetNodeOrNull<Node>(\"Banner\")?.QueueFree();",
        })
        {
            int next = fixture.IndexOf(node, position, StringComparison.Ordinal);
            Assert.True(next > position, "Missing or reordered bootstrap retirement: " + node);
            position = next;
        }
    }

    [Fact]
    public void SmokeHudStatePreservesTheOriginalDeterministicFixture()
    {
        string pureFixture = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/diagnostics/Framework/SmokeHudFixtureState.cs"));
        Assert.DoesNotContain("using Godot;", pureFixture, StringComparison.Ordinal);
        Assert.DoesNotContain("Node3D", pureFixture, StringComparison.Ordinal);
        var syntax = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(pureFixture).GetRoot();
        Assert.DoesNotContain(
            syntax.DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax>(),
            identifier => identifier.Identifier.ValueText == "Godot");
        HudPresentationState state = SmokeHudFixtureState.Create();

        Assert.Equal(new HudHealth(3, 6, "nanotech", HudLifeState.Alive), state.Health);
        Assert.Equal(new HudCurrency("bolts", 12_450), state.Bolts);
        Assert.Equal(new HudWeapon("hud.fixture.vector-blaster", "Vector Blaster", new HudAmmo(7, 12)), state.CurrentWeapon);
        Assert.Equal(new HudContextPrompt("hud-fixture-console", "interact", "Activate test console", Progress: 0.42), state.ContextPrompt);
        Assert.Null(state.Air);
    }
}
