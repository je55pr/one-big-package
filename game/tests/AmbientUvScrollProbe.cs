using System;
using System.Linq;
using Godot;
using OBP.Core;
using OBP.Core.Math;
using OBP.Godot;
using OBP.IO;
using OBP.RAC3;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>Headless Godot integration fixture for issue #172's ambient material binding.</summary>
public partial class AmbientUvScrollProbe : Node
{
    public override void _Ready()
    {
        try
        {
            GroupedScrollKeepsOtherGroupsStill();
            UngroupedScrollRetainsSharedBinding();
            IndependentlyAnimatedGroupsStayIndependent();
            EverySharedBindingHasAScrollProfile();
            VerifyRetailSkyGroupIfAvailable();
            GD.Print("[ambient-uv-scroll-smoke] PASS: grouped, ungrouped and shared material tick behaviour");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PushError("[ambient-uv-scroll-smoke] FAIL: " + ex);
            GetTree().Quit(1);
        }
    }

    private static RuntimeWorld Fixture(params RuntimeAmbientAnimation[] animations)
    {
        static RuntimeMesh Mesh(string? group) => new(
            "sky", 3,
            Positions: [0d, 0d, 0d, 1d, 0d, 0d, 0d, 1d, 0d],
            Uvs: [0f, 0f, 1f, 0f, 0f, 1f],
            Indices: [0, 1, 2],
            PresentationGroup: group);

        return new RuntimeWorld(
            Game: ObpSourceGame.Synthetic,
            BuildId: "ambient-uv-scroll-fixture",
            LevelId: 0,
            PlanetName: null,
            LocationName: null,
            Meshes: [Mesh("A"), Mesh("A"), Mesh("B"), Mesh(null)],
            Textures: [new RuntimeTexture("sky", 3, 2, 2,
            [
                255, 255, 255, 255, 255, 255, 255, 255,
                255, 255, 255, 255, 255, 255, 255, 255
            ])],
            MaterialCount: 1,
            CollisionMeshes: [],
            Bounds: new ObpBounds(Vec3.Zero, new Vec3(1, 1, 1)),
            Environment: null,
            Ship: null,
            AmbientAnimations: animations);
    }

    private (WorldHost Host, RuntimeWorldScene.Result Scene) Build(params RuntimeAmbientAnimation[] animations)
    {
        var parent = new Node3D { Name = "AmbientFixture" };
        AddChild(parent);
        var host = new WorldHost();
        var scene = host.Load(this, parent, Fixture(animations), "Fixture", new WorldHost.Options
        {
            ManageEnvironment = false,
            IncludeCollision = false,
        });
        return (host, scene);
    }

    private static StandardMaterial3D Material(RuntimeWorldScene.Result scene, string? group)
    {
        var matching = scene.AmbientMeshBindings!.Where(binding => binding.PresentationGroup == group).ToArray();
        if (matching.Length == 0) throw new Exception($"Missing group: {group ?? "(ungrouped)"}");
        var first = (StandardMaterial3D)matching[0].Mesh.MaterialOverride;
        if (matching.Any(binding => !ReferenceEquals(first, binding.Mesh.MaterialOverride)))
            throw new Exception($"Meshes in group {group} should share a material when their animation matches");
        return first;
    }

    private static void Offset(StandardMaterial3D material, float u, float v)
    {
        if (material.Uv1Offset.DistanceTo(new Vector3(u, v, 0)) > 0.00001f)
            throw new Exception($"UV offset was {material.Uv1Offset}; expected {u}, {v}");
    }

    private void GroupedScrollKeepsOtherGroupsStill()
    {
        var (host, scene) = Build(new RuntimeAmbientAnimation(
            "sky", 3, RuntimeAmbientAnimationKind.UvScroll, (0.1, 0.2, 0), TargetGroup: "A"));
        var a = Material(scene, "A");
        var b = Material(scene, "B");
        var none = Material(scene, null);
        if (ReferenceEquals(a, b) || !ReferenceEquals(b, none))
            throw new Exception("Scrolling group A must isolate its material, but leave other materials shared");
        host.Tick(2, Vector3.Zero);
        Offset(a, 0.2f, 0.4f);
        Offset(b, 0f, 0f);
        Offset(none, 0f, 0f);
        host.Unload();
    }

    private void UngroupedScrollRetainsSharedBinding()
    {
        var (host, scene) = Build(new RuntimeAmbientAnimation(
            "sky", 3, RuntimeAmbientAnimationKind.UvScroll, (0.125, 0.25, 0)));
        var a = Material(scene, "A");
        var b = Material(scene, "B");
        var none = Material(scene, null);
        if (!ReferenceEquals(a, b) || !ReferenceEquals(b, none))
            throw new Exception("Same ungrouped UV scroll should keep the factory's material sharing");
        host.Tick(1, Vector3.Zero);
        Offset(a, 0.125f, 0.25f);
        Offset(b, 0.125f, 0.25f);
        Offset(none, 0.125f, 0.25f);
        host.Unload();
    }

    private void IndependentlyAnimatedGroupsStayIndependent()
    {
        var (host, scene) = Build(
            new RuntimeAmbientAnimation("sky", 3, RuntimeAmbientAnimationKind.UvScroll, (0.1, 0, 0), TargetGroup: "A"),
            new RuntimeAmbientAnimation("sky", 3, RuntimeAmbientAnimationKind.UvScroll, (0, 0.2, 0), TargetGroup: "B"));
        var a = Material(scene, "A");
        var b = Material(scene, "B");
        var none = Material(scene, null);
        if (ReferenceEquals(a, b) || ReferenceEquals(a, none) || ReferenceEquals(b, none))
            throw new Exception("Different UV scroll profiles must have independent materials");
        host.Tick(2, Vector3.Zero);
        Offset(a, 0.2f, 0f);
        Offset(b, 0f, 0.4f);
        Offset(none, 0f, 0f);
        host.Unload();
    }

    /// <summary>Exercise the partition path where every material user has an animation profile.</summary>
    private void EverySharedBindingHasAScrollProfile()
    {
        var (host, scene) = Build(
            new RuntimeAmbientAnimation("sky", 3, RuntimeAmbientAnimationKind.UvScroll, (0.05, 0.05, 0)),
            new RuntimeAmbientAnimation("sky", 3, RuntimeAmbientAnimationKind.UvScroll, (0.1, 0, 0), TargetGroup: "A"),
            new RuntimeAmbientAnimation("sky", 3, RuntimeAmbientAnimationKind.UvScroll, (0, 0.2, 0), TargetGroup: "B"));
        var a = Material(scene, "A");
        var b = Material(scene, "B");
        var none = Material(scene, null);
        if (ReferenceEquals(a, b) || ReferenceEquals(a, none) || ReferenceEquals(b, none))
            throw new Exception("Every distinct scroll profile needs its own material");
        host.Tick(2, Vector3.Zero);
        Offset(a, 0.2f, 0f);
        Offset(b, 0f, 0.4f);
        Offset(none, 0.1f, 0.1f);
        host.Unload();
    }

    /// <summary>Exercise a real UYA sky's presentation groups with a synthetic UV rate.
    /// Retail supplies the geometry/group identities, not this test animation.</summary>
    private void VerifyRetailSkyGroupIfAvailable()
    {
        string? iso = System.Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        if (string.IsNullOrWhiteSpace(iso) || !System.IO.File.Exists(iso))
        {
            GD.Print("[ambient-uv-scroll-smoke] SKIP: no retail UYA source configured");
            return;
        }

        using var reader = new FileRandomAccessReader(iso);
        RuntimeWorld retail = Rac3WorldImport.Build(reader, 1);
        var sky = retail.Meshes.Where(m => m.AssetKind == "sky" && m.TextureId >= 0 &&
            m.PresentationGroup is { Length: > 0 }).ToArray();
        var sharedTexture = sky.GroupBy(m => m.TextureId)
            .FirstOrDefault(g => g.Select(m => m.PresentationGroup).Distinct().Count() > 1);
        var chosen = sharedTexture?.FirstOrDefault() ?? sky.FirstOrDefault()
            ?? throw new Exception("UYA retail sky contains no textured presentation groups");
        string group = chosen.PresentationGroup!;
        int textureId = chosen.TextureId;

        RuntimeWorld skyWorld = retail with
        {
            Meshes = sky,
            Textures = retail.Textures.Where(t => t.AssetKind == "sky").ToArray(),
            CollisionMeshes = [],
            AnimatedMeshes = [],
            DynamicObjects = [],
            AmbientAnimations = [new RuntimeAmbientAnimation(
                "sky", textureId, RuntimeAmbientAnimationKind.UvScroll, (0.25, 0.125, 0),
                TargetGroup: group)]
        };

        var parent = new Node3D { Name = "RetailSkyFixture" };
        AddChild(parent);
        var host = new WorldHost();
        var scene = host.Load(this, parent, skyWorld, "RetailSky", new WorldHost.Options
        {
            ManageEnvironment = false,
            IncludeCollision = false,
        });
        var bindings = scene.AmbientMeshBindings!;
        var targeted = bindings.Where(b => b.AssetKind == "sky" &&
            b.TextureId == textureId && b.PresentationGroup == group).ToArray();
        var other = bindings.Where(b => b.AssetKind == "sky" &&
            b.TextureId == textureId && b.PresentationGroup != group).ToArray();
        if (targeted.Length == 0) throw new Exception("Retail group was lost during scene construction");

        host.Tick(1, Vector3.Zero);
        foreach (var binding in targeted)
            Offset((StandardMaterial3D)binding.Mesh.MaterialOverride, 0.25f, 0.125f);
        foreach (var binding in other)
            Offset((StandardMaterial3D)binding.Mesh.MaterialOverride, 0f, 0f);
        host.Unload();
        if (other.Length == 0)
        {
            GD.Print($"[ambient-uv-scroll-smoke] Retail UYA sky: targeted group {group} offset verified; " +
                     "SKIP cross-group isolation: no sky texture is shared across presentation groups");
        }
        else
        {
            GD.Print($"[ambient-uv-scroll-smoke] Retail UYA sky: group {group}, texture {textureId}, " +
                     $"{targeted.Length} targeted and {other.Length} same-texture other-group meshes verified");
        }
    }
}
