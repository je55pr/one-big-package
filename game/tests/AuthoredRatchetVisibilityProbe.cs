using System;
using System.Linq;
using Godot;
using OBP.Core;
using OBP.Core.Math;
using OBP.Godot;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>Godot-hosted regression for duplicate authored Ratchet presentation in R&C1.</summary>
public partial class AuthoredRatchetVisibilityProbe : Node
{
    public override void _Ready()
    {
        RuntimeWorldScene.Result? scene = null;
        try
        {
            scene = RuntimeWorldScene.Build(Fixture());
            var meshes = scene.AnimatedMeshes ?? throw new InvalidOperationException("Animated scene was not built.");
            Require(meshes.Count == 4 && meshes.All(mesh => mesh.Instance.Visible),
                "Expected four initially visible authored mesh instances.");
            Require(meshes.Count(mesh => mesh.Name.StartsWith("ratchet_i", StringComparison.Ordinal)) == 2 &&
                    meshes.Any(mesh => mesh.Name == "ratchet_i0_t0") &&
                    meshes.Any(mesh => mesh.Name == "ratchet_i1_t0"),
                "Two explicitly named authored Ratchet duplicates are required to exercise hiding.");

            var avatar = new PlayerAvatarPresentationHost();
            avatar.HideAuthoredWorldRatchetPresentation(Destination(ObpSourceGame.Rac1), scene);
            foreach (var mesh in meshes)
            {
                bool duplicate = mesh.Name.StartsWith("ratchet_i", StringComparison.Ordinal);
                Require(mesh.Instance.Visible == !duplicate,
                    $"R&C1 visibility filter failed: {mesh.Name}.");
            }

            foreach (var mesh in meshes) mesh.Instance.Visible = true;
            avatar.HideAuthoredWorldRatchetPresentation(Destination(ObpSourceGame.Rac2), scene);
            Require(meshes.All(mesh => mesh.Instance.Visible), "Non-R&C1 destination hid authored world surfaces.");

            avatar.HideAuthoredWorldRatchetPresentation(null, scene);
            Require(meshes.All(mesh => mesh.Instance.Visible), "Missing destination hid authored world surfaces.");

            avatar.HideAuthoredWorldRatchetPresentation(Destination(ObpSourceGame.Rac1), null);
            avatar.HideAuthoredWorldRatchetPresentation(Destination(ObpSourceGame.Rac1), scene with { AnimatedMeshes = null });
            Require(meshes.All(mesh => mesh.Instance.Visible), "Missing scene or animation list altered visibility.");

            GD.Print("[authored-ratchet-visibility-smoke] PASS: R&C1 duplicate filter, source gate, null scene and actual Godot instance visibility");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError("[authored-ratchet-visibility-smoke] FAIL: " + error);
            GetTree().Quit(1);
        }
        finally
        {
            scene?.Root.Free();
        }
    }

    private static ObpDestination Destination(ObpSourceGame game) =>
        new($"{game}:LEVEL0", game, "visibility-fixture", "LEVEL0", null,
            "Test World", null, null, ObpDestinationKind.Planet);

    private static RuntimeAnimatedMesh Mesh(string name) =>
        new(name, "moby", 0, [0f, 0f, 1f, 0f, 0f, 1f], [0, 1, 2], [],
            [[0d, 0d, 0d, 1d, 0d, 0d, 0d, 1d, 0d]], 60f);

    private static RuntimeWorld Fixture() => new(
        Game: ObpSourceGame.Rac1,
        BuildId: "visibility-fixture",
        LevelId: 0,
        PlanetName: "Test World",
        LocationName: null,
        Meshes: [],
        Textures: [new RuntimeTexture("moby", 0, 2, 2,
            [255, 255, 255, 255, 255, 255, 255, 255,
             255, 255, 255, 255, 255, 255, 255, 255])],
        MaterialCount: 1,
        CollisionMeshes: [],
        Bounds: new ObpBounds(Vec3.Zero, new Vec3(2, 2, 2)),
        Environment: null,
        Ship: null,
        AnimatedMeshes: [Mesh("ratchet_i0_t0"), Mesh("ratchet_i1_t0"),
                         Mesh("ratchet_body"), Mesh("other_i0_t0")]);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
