using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC1.Player;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>Source-tagged horizontal water surfaces and sampled contact height.</summary>
internal sealed class Rac1NovalisWaterMechanism : Rac1AuthoredMechanism
{
    private Rac1NovalisWaterSurfaces? _water;
    internal bool Configured => _water is not null;

    internal override void Configure(RuntimeWorld world, RuntimeWorldScene.Result scene)
    {
        if (world.Game != ObpSourceGame.Rac1 || world.LevelId != 1) return;
        _water = Rac1NovalisWaterSurfaces.FromWorld(world);
        GD.Print($"[rac1-novalis-water] {_water.SurfaceTriangleCount} horizontal water-tagged source faces indexed");
    }

    internal float? Sample(Vector3 scenePosition) =>
        _water is { } water &&
        water.TrySample(-scenePosition.X, scenePosition.Z, out double height)
            ? (float)height
            : null;

    // Host collision-pass-through policy, shared by scene preparation before
    // the lease is activated. Steep class-zero walls remain physical.
    internal static bool AdmitCollisionFace(int material, Vector3 a, Vector3 b, Vector3 c)
    {
        if (material is < 0 or > 255 ||
            Rac1CollisionFaceSemantics.Decode(material).SurfaceClass != 0)
            return true;
        Vector3 normal = (b - a).Cross(c - a);
        return Mathf.Abs(normal.Y) < new Vector2(normal.X, normal.Z).Length();
    }

    public override void Dispose() => _water = null;
}
