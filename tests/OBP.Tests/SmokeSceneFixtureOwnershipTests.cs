using OBP.Game;
using OBP.Runtime.Presentation;

namespace OBP.Tests;

public sealed class SmokeSceneFixtureOwnershipTests
{
    [Fact]
    public void RootDelegatesSyntheticSceneGeometryAndHudFixtureToDedicatedOwner()
    {
        string root = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/scripts/OBPGame.cs"));
        string fixture = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/diagnostics/GodotSmokeSceneFixture.cs"));
        int start = root.IndexOf("private void BuildScene(string scene)", StringComparison.Ordinal);
        int end = root.IndexOf("private bool Rac1PlanetMapOpen", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);

        string glue = root[start..end];
        Assert.Contains("_mode = Mode.Smoke;", glue, StringComparison.Ordinal);
        Assert.Contains("_sceneKind = scene;", glue, StringComparison.Ordinal);
        Assert.Contains("EnsurePlainEnvironment();", glue, StringComparison.Ordinal);
        Assert.Contains("GodotSmokeSceneFixture.Build(this, _worldRoot, _ui, _camera, scene)", glue, StringComparison.Ordinal);
        Assert.Contains("EnsurePlayerHud();", glue, StringComparison.Ordinal);
        Assert.Contains("_playerHud!.Render(_hudState.BeginSession(SmokeHudFixtureState.Create()))", glue, StringComparison.Ordinal);
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
    public void SmokeHudStatePreservesTheOriginalDeterministicFixture()
    {
        string pureFixture = File.ReadAllText(Path.Combine(RepoPaths.Root, "game/diagnostics/SmokeHudFixtureState.cs"));
        Assert.DoesNotContain("using Godot;", pureFixture, StringComparison.Ordinal);
        Assert.DoesNotContain("Node3D", pureFixture, StringComparison.Ordinal);
        HudPresentationState state = SmokeHudFixtureState.Create();

        Assert.Equal(new HudHealth(3, 6, "nanotech", HudLifeState.Alive), state.Health);
        Assert.Equal(new HudCurrency("bolts", 12_450), state.Bolts);
        Assert.Equal(new HudWeapon("hud.fixture.vector-blaster", "Vector Blaster", new HudAmmo(7, 12)), state.CurrentWeapon);
        Assert.Equal(new HudContextPrompt("hud-fixture-console", "interact", "Activate test console", Progress: 0.42), state.ContextPrompt);
        Assert.Null(state.Air);
    }
}
