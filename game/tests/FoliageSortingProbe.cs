using System;
using System.Linq;
using Godot;
using OBP.Core;
using OBP.Core.Math;
using OBP.Godot;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>Headless native Godot material/placement regression for Novalis foliage issue #399.</summary>
public partial class FoliageSortingProbe : Node
{
    public override void _Ready()
    {
        try
        {
            VerifyFoliageMaterialDepthAndSortOrigin();
            GD.Print("[foliage-sorting-smoke] PASS: blended canopies get local sort origins; opaque trunks and TIEs remain unchanged");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError("[foliage-sorting-smoke] FAIL: " + exception);
            GetTree().Quit(1);
        }
    }

    private static RuntimeMesh Surface(string kind, int tex, double x) => new(
        kind, tex,
        Positions: [x, 2d, 7d, x + 2d, 2d, 7d, x, 4d, 7d],
        Uvs: [0f, 0f, 1f, 0f, 0f, 1f],
        Indices: [0, 1, 2],
        MaterialPresentation: new(AlphaBlendEnabled: true, PreferPerPlacementFoliageDepth: kind == "shrub"));

    private static RuntimeTexture Texture(string kind, int tex, bool partial) =>
        new(kind, tex, 2, 2,
        [
            200, 30, 20, 255,
            200, 30, 20, partial ? (byte)128 : (byte)255,
            200, 30, 20, 255,
            200, 30, 20, 255,
        ]);

    private void VerifyFoliageMaterialDepthAndSortOrigin()
    {
        var world = new RuntimeWorld(
            Game: ObpSourceGame.Rac1,
            BuildId: "foliage-sorting-fixture",
            LevelId: 0,
            PlanetName: null,
            LocationName: null,
            Meshes: [
                Surface("shrub", 14, 20),
                Surface("shrub", 14, 80),
                Surface("shrub", 15, 60),
                Surface("shrub", 16, 120) with { MaterialPresentation = new(
                    AlphaBlendEnabled: true, PreferPerPlacementFoliageDepth: true,
                    BlendEquation: OBP.Runtime.Presentation.RuntimeBlendEquation.FixedAlpha,
                    FixedAlphaFactor: 0.5) },
                Surface("shrub", 17, 160) with { MaterialPresentation = new(
                    AlphaBlendEnabled: true, PreferPerPlacementFoliageDepth: true,
                    BlendEquation: OBP.Runtime.Presentation.RuntimeBlendEquation.AdditiveSourceAlpha) },
                Surface("shrub", 18, 200) with { MaterialPresentation = new(
                    AlphaBlendEnabled: true, PreferPerPlacementFoliageDepth: true,
                    BlendEquation: OBP.Runtime.Presentation.RuntimeBlendEquation.AdditiveFixedAlpha,
                    FixedAlphaFactor: 0.5) },
                Surface("shrub", 19, 240) with { MaterialPresentation = new(
                    AlphaBlendEnabled: true, PreferPerPlacementFoliageDepth: true,
                    PreferFoliageCutout: true) },
                Surface("tie", 14, 40),
            ],
            Textures: [
                Texture("shrub", 14, partial: true),
                Texture("shrub", 15, partial: false),
                Texture("shrub", 16, partial: false),
                Texture("shrub", 17, partial: false),
                Texture("shrub", 18, partial: false),
                Texture("shrub", 19, partial: true),
                Texture("tie", 14, partial: true),
            ],
            MaterialCount: 7,
            CollisionMeshes: [],
            Bounds: new ObpBounds(Vec3.Zero, new Vec3(100, 10, 10)),
            Environment: null,
            Ship: null);

        var container = new Node3D { Name = "FoliageFixture" };
        AddChild(container);
        var host = new WorldHost();
        var scene = host.Load(this, container, world, "Fixture", new WorldHost.Options
        {
            ManageEnvironment = false,
            IncludeCollision = false,
        });

        var canopies = scene.AmbientMeshBindings!
            .Where(binding => binding.AssetKind == "shrub" && binding.TextureId == 14)
            .Select(binding => binding.Mesh).ToArray();
        if (canopies.Length != 2)
            throw new InvalidOperationException("Two independent canopy meshes must be built.");
        var left = canopies.OrderBy(mesh => mesh.Position.X).ToArray();
        var alpha = (StandardMaterial3D)left[0].MaterialOverride;
        if (alpha.Transparency != BaseMaterial3D.TransparencyEnum.Alpha ||
            !ReferenceEquals(alpha, left[1].MaterialOverride))
            throw new InvalidOperationException("Canopy alpha should be preserved and cached.");
        if (left.Any(mesh => mesh.Position == Vector3.Zero) ||
            left[0].Position.DistanceTo(left[1].Position) < 40f)
            throw new InvalidOperationException("Canopies must have individual sorting pivots.");

        foreach (var mesh in left)
        {
            var originalX = -mesh.Position.X - 1f;
            var aabb = mesh.GetAabb();
            if (Mathf.Abs(aabb.GetCenter().X) > 0.001f ||
                Mathf.Abs(aabb.GetCenter().Y) > 0.001f ||
                Mathf.Abs(originalX - 20f) > 0.001f && Mathf.Abs(originalX - 80f) > 0.001f)
                throw new InvalidOperationException("Canopy pivot must preserve world coordinates.");
        }

        var trunk = scene.AmbientMeshBindings!.Single(binding =>
            binding.AssetKind == "shrub" && binding.TextureId == 15).Mesh;
        var bark = (StandardMaterial3D)trunk.MaterialOverride;
        if (bark.Transparency != BaseMaterial3D.TransparencyEnum.Disabled ||
            trunk.Position != Vector3.Zero)
            throw new InvalidOperationException("Opaque bark should write depth without repositioning.");

        var cutoutLeaf = scene.AmbientMeshBindings!.Single(binding =>
            binding.AssetKind == "shrub" && binding.TextureId == 19).Mesh;
        var cutout = (StandardMaterial3D)cutoutLeaf.MaterialOverride;
        if (cutout.Transparency != BaseMaterial3D.TransparencyEnum.AlphaScissor ||
            Mathf.Abs(cutout.AlphaScissorThreshold - 0.5f) > 0.0001f ||
            cutoutLeaf.Position != Vector3.Zero)
            throw new InvalidOperationException("Authorised foliage cutout must write depth without alpha-sort recentering.");

        foreach (var id in new[] { 16, 17, 18 })
        {
            var blendMesh = scene.AmbientMeshBindings!.Single(binding =>
                binding.AssetKind == "shrub" && binding.TextureId == id).Mesh;
            var blendMaterial = (StandardMaterial3D)blendMesh.MaterialOverride;
            if (blendMaterial.Transparency != BaseMaterial3D.TransparencyEnum.Alpha)
                throw new InvalidOperationException($"Fixed/additive alpha shrub {id} must stay transparent.");
        }

        var tie = scene.AmbientMeshBindings!.Single(binding => binding.AssetKind == "tie").Mesh;
        var tieMaterial = (StandardMaterial3D)tie.MaterialOverride;
        if (tieMaterial.Transparency != BaseMaterial3D.TransparencyEnum.Alpha ||
            tie.Position != Vector3.Zero)
            throw new InvalidOperationException("Unrelated TIE rendering must stay unchanged.");

        host.Unload();
    }
}
