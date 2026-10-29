using System;
using System.Linq;
using Godot;
using OBP.Core;
using OBP.Core.Math;
using OBP.Godot;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>Portable headless Godot regression for visible Moby surface eligibility on every render path.</summary>
public partial class MobyMateriallessProbe : Node
{
    private static readonly double[] Positions = [0, 0, 0, 1, 0, 0, 0, 1, 0];
    private static readonly float[] Uvs = [0, 0, 1, 0, 0, 1];
    private static readonly int[] Indices = [0, 1, 2];
    private static readonly float[] Colours = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1];

    public override void _Ready()
    {
        try
        {
            VerifyGodotVisibility();
            GD.Print("[moby-materialless-smoke] PASS: static, dynamic and animated visibility, provenance, identity and totals");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError("[moby-materialless-smoke] FAIL: " + error);
            GetTree().Quit(1);
        }
    }

    private static RuntimeMesh Static(int texture, float[]? colours = null, bool authored = true, string kind = "moby") =>
        new(kind, texture, Positions, Uvs, Indices, colours, VertexColoursAreAuthored: authored);

    private static RuntimeObjectMesh Dynamic(int texture, float[]? colours = null, bool authored = true) =>
        new("moby", texture, Positions, Uvs, Indices, colours, VertexColoursAreAuthored: authored);

    private static RuntimeAnimatedMesh Animated(string name, int texture, float[]? colours = null, bool authored = true) =>
        new(name, "moby", texture, Uvs, Indices, colours ?? [], [Positions, Positions], 5f,
            VertexColoursAreAuthored: authored);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private void VerifyGodotVisibility()
    {
        var actor = new RuntimeDynamicObject(ObpSourceGame.Rac2, 7816, 641, null,
            "moby:7816", "table:2:moby:641", new RuntimeObjectTransform(
                [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]),
            [Dynamic(-1), Dynamic(-1, Colours, authored: false), Dynamic(1), Dynamic(-1, Colours)]);

        var world = new RuntimeWorld(ObpSourceGame.Rac2, "materialless-fixture", 2, null, null,
            Meshes: [Static(-1), Static(-1, Colours, authored: false), Static(1),
                     Static(-1, Colours), Static(-1, kind: "tie")],
            Textures: [new RuntimeTexture("moby", 1, 1, 1, [255, 255, 255, 255])],
            MaterialCount: 1,
            CollisionMeshes: [],
            Bounds: new ObpBounds(Vec3.Zero, new Vec3(2, 2, 2)),
            Environment: null,
            Ship: null,
            AnimatedMeshes: [Animated("anim-none", -1), Animated("anim-lit-only", -1, Colours, authored: false),
                             Animated("anim-textured", 1), Animated("anim-coloured", -1, Colours)],
            DynamicObjects: [actor]);

        var scene = RuntimeWorldScene.Build(world, "MobyFixture", new RuntimeWorldScene.Options
        {
            IncludeCollision = false,
            IncludeSky = false,
        });
        AddChild(scene.Root);
        try
        {
            // Static mesh nodes are added before animated nodes; Godot may
            // uniquify repeated moby_-1 names when they enter the scene tree.
            var directMeshes = scene.Root.GetChildren().OfType<MeshInstance3D>().ToArray();
            Require(directMeshes.Length == 9, "All static and animated render nodes must survive");
            var staticNodes = directMeshes.Take(5).ToArray();
            Require(!staticNodes[0].Visible && !staticNodes[1].Visible,
                "Materialless static Mobies, including generated-light-only, must be hidden");
            Require(staticNodes[2].Visible && staticNodes[3].Visible && staticNodes[4].Visible,
                "Textured, authored-colour and non-Moby static surfaces must remain visible");

            var dynamic = scene.DynamicObjectNodes?.Single()
                ?? throw new InvalidOperationException("Dynamic actor was not built");
            Require(ReferenceEquals(dynamic.Source, actor), "Source actor identity lost");
            var dynamicNodes = dynamic.Root.GetChildren().OfType<MeshInstance3D>().ToArray();
            Require(dynamicNodes.Length == 4, "All source dynamic surfaces must survive");
            Require(!dynamicNodes[0].Visible && !dynamicNodes[1].Visible &&
                    dynamicNodes[2].Visible && dynamicNodes[3].Visible,
                "Dynamic presentation must distinguish authored colours from synthetic lighting");

            var animated = scene.AnimatedMeshes?.ToArray() ?? [];
            Require(animated.Length == 4, "All source animated handles must survive");
            Require(!animated[0].Instance.Visible && !animated[1].Instance.Visible &&
                    animated[2].Instance.Visible && animated[3].Instance.Visible,
                "Animated presentation must distinguish authored colours from synthetic lighting");
            Require(animated.All(a => a.FrameCount == 2), "Native animation frames must remain available");
            RuntimeWorldScene.AdvanceAnimated(scene, 0.2);

            // Three visible static (moby, authored-colour moby, tie),
            // two dynamic and two animated surfaces. Invisible nodes are retained
            // but must not be included in render totals or ambient bindings.
            Require(scene.MeshInstances == 5 && scene.AnimatedMobies == 2 &&
                    scene.Triangles == 7 && scene.DynamicObjects == 1,
                $"Wrong visible totals: mesh={scene.MeshInstances} animated={scene.AnimatedMobies} tris={scene.Triangles}");
            Require(scene.AmbientMeshBindings?.Count == 7, "Only seven visible surfaces should have ambient bindings");
        }
        finally
        {
            scene.Root.QueueFree();
        }
    }
}
