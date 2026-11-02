using Godot;
using OBP.Runtime.Presentation;

namespace OBP.Game;

/// <summary>
/// Asset-free host smoke-scene geometry and deterministic HUD fixture facts.
/// Application mode, scene-tree lifetime and HUD session remain with the root.
/// </summary>
internal static class GodotSmokeSceneFixture
{
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

    internal static HudPresentationState HudState()
    {
        return new(
            Health: new HudHealth(3, 6, "nanotech", HudLifeState.Alive),
            Bolts: new HudCurrency("bolts", 12_450),
            CurrentWeapon: new HudWeapon(
                PresentationKey: "hud.fixture.vector-blaster",
                NameKey: "Vector Blaster",
                Ammo: new HudAmmo(7, 12)),
            ContextPrompt: new HudContextPrompt(
                PromptId: "hud-fixture-console",
                ActionId: "interact",
                MessageKey: "Activate test console",
                Progress: 0.42));
    }
}
