using Godot;
using OBP.Core;
using OBP.IO;

namespace OBP.Game;

/// <summary>
/// Asset-free bootstrap environment and smoke-scene geometry.
/// Application mode, world-lease lifetime and HUD session remain with the root.
/// </summary>
internal static class GodotSmokeSceneFixture
{
    internal static void BuildApplicationScene(Node3D root, GodotApplicationPresentation application,
        OBP.Runtime.Presentation.HudStateAdapter hud, string scene)
    {
        EnsurePlainEnvironment(root);
        Build(root, application.WorldRoot, application.Ui, application.Camera, scene);
        if (scene.Equals("hud", StringComparison.OrdinalIgnoreCase))
        {
            application.Ui.GetNode<Label>("Banner").QueueFree();
            if (!Rac1RetailUiAssets.IsLoaded)
            {
                string source = CommandLineArgs.Parse(OS.GetCmdlineUserArgs()).Rac1Iso
                    ?? throw new ArgumentException("The HUD fixture requires --rac1-iso for its source icons.");
                using var disc = new FileRandomAccessReader(source);
                Rac1RetailUiAssets.Load(disc);
                WeaponMenuIcons.Load(ObpSourceGame.Rac1, disc);
                UiTheme.Apply(root.GetTree().Root);
            }
            application.EnsurePlayerHud();
            application.PlayerHud!.Render(hud.BeginSession(SmokeHudFixtureState.Create()));
        }
    }
    /// <summary>Create the plain bootstrap backdrop only if it does not already exist.</summary>
    internal static void EnsurePlainEnvironment(Node3D root)
    {
        var existing = root.GetNodeOrNull<Node>("PlainEnvironment");
        if (existing is not null)
        {
            if (!existing.IsQueuedForDeletion())
                return;
            // QueueFree is deferred. Detach the old node now so its name can
            // be reused when the title/source picker opens in the same frame.
            root.RemoveChild(existing);
        }

        root.AddChild(new WorldEnvironment
        {
            Name = "PlainEnvironment",
            Environment = new global::Godot.Environment
            {
                BackgroundMode = global::Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.04f, 0.05f, 0.08f),
                AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(1, 1, 1),
                AmbientLightEnergy = 1.0f,
            },
        });
    }

    /// <summary>Retire only bootstrap-owned nodes before a prepared world is activated.</summary>
    internal static void RetireBootstrap(Node3D root, Node3D worldRoot, CanvasLayer ui)
    {
        root.GetNodeOrNull<Node>("PlainEnvironment")?.QueueFree();
        root.GetNodeOrNull<Node>("Sun")?.QueueFree();
        worldRoot.GetNodeOrNull<Node>("Floor")?.QueueFree();
        worldRoot.GetNodeOrNull<Node>("SmokeCube")?.QueueFree();
        ui.GetNodeOrNull<Node>("Banner")?.QueueFree();
    }

    internal static void Build(Node3D root, Node3D worldRoot, CanvasLayer ui, Camera3D camera, string scene)
    {
        var sun = new DirectionalLight3D { Name = "Sun", RotationDegrees = new Vector3(-55, -35, 0) };
        root.AddChild(sun);

        worldRoot.AddChild(new MeshInstance3D
        {
            Name = "Floor",
            Mesh = new PlaneMesh { Size = new Vector2(12, 12) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.2f, 0.24f) },
        });
        worldRoot.AddChild(new MeshInstance3D
        {
            Name = "SmokeCube",
            Mesh = new BoxMesh { Size = new Vector3(2, 2, 2) },
            Position = new Vector3(0, 1, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.45f, 0.15f) },
        });

        camera.Position = new Vector3(4.5f, 3.5f, 5.5f);
        camera.LookAt(new Vector3(0, 0.8f, 0), Vector3.Up);
        ui.AddChild(new Label { Name = "Banner", Text = $"One Big Package — {scene}", Position = new Vector2(16, 12) });

    }

}
