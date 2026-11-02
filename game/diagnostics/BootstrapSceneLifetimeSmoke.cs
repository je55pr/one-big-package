using Godot;

namespace OBP.Game.Diagnostics;

/// <summary>Asset-free Godot lifecycle regression for the owned bootstrap backdrop.</summary>
public partial class BootstrapSceneLifetimeSmoke : Node3D
{
    private const string EnvironmentName = "PlainEnvironment";

    public override void _Ready() => CallDeferred(MethodName.Run);

    // Godot invokes this void callback; the async task catches and reports its failures.
    private void Run() => _ = RunAsync();

    private async Task RunAsync()
    {
        try
        {
            var worldRoot = new Node3D { Name = "WorldRoot" };
            var ui = new CanvasLayer { Name = "UI" };
            AddChild(worldRoot);
            AddChild(ui);

            GodotSmokeSceneFixture.EnsurePlainEnvironment(this);
            var camera = new Camera3D { Name = "Camera3D" };
            AddChild(camera);
            GodotSmokeSceneFixture.Build(this, worldRoot, ui, camera, "lifetime");
            var sun = GetNode<DirectionalLight3D>("Sun");
            var floor = worldRoot.GetNode<MeshInstance3D>("Floor");
            var smokeCube = worldRoot.GetNode<MeshInstance3D>("SmokeCube");
            var banner = ui.GetNode<Label>("Banner");
            var first = GetNode<WorldEnvironment>(EnvironmentName);
            Require(!first.IsQueuedForDeletion(), "new environment was queued for deletion");
            GodotSmokeSceneFixture.EnsurePlainEnvironment(this);
            Require(ReferenceEquals(first, GetNode<WorldEnvironment>(EnvironmentName)),
                "active environment was unnecessarily replaced");

            GodotSmokeSceneFixture.RetireBootstrap(this, worldRoot, ui);
            Require(first.IsQueuedForDeletion(), "bootstrap cleanup did not queue the environment");
            Require(sun.IsQueuedForDeletion() && floor.IsQueuedForDeletion()
                && smokeCube.IsQueuedForDeletion() && banner.IsQueuedForDeletion(),
                "bootstrap cleanup did not queue every fixture-owned node");
            GodotSmokeSceneFixture.EnsurePlainEnvironment(this);

            var second = GetNode<WorldEnvironment>(EnvironmentName);
            Require(!ReferenceEquals(first, second), "queued environment was reused");
            Require(!second.IsQueuedForDeletion(), "replacement environment is already queued");
            Require(first.GetParent() is null, "queued node still holds the old name in its parent");
            GodotSmokeSceneFixture.EnsurePlainEnvironment(this);
            Require(ReferenceEquals(second, GetNode<WorldEnvironment>(EnvironmentName)),
                "same-frame replacement did not become the active environment");

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!GodotObject.IsInstanceValid(first), "old queued environment survived frame cleanup");
            Require(!GodotObject.IsInstanceValid(sun) && !GodotObject.IsInstanceValid(floor)
                && !GodotObject.IsInstanceValid(smokeCube) && !GodotObject.IsInstanceValid(banner),
                "a retired fixture-owned node survived frame cleanup");
            Require(GodotObject.IsInstanceValid(second) && !second.IsQueuedForDeletion()
                && ReferenceEquals(second, GetNode<WorldEnvironment>(EnvironmentName)),
                "replacement was lost after deferred cleanup");

            GD.Print("[bootstrap-lifetime-smoke] PASS: reuse, same-frame replacement, deferred deletion");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PrintErr("[bootstrap-lifetime-smoke] FAIL: " + error);
            GetTree().Quit(1);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}