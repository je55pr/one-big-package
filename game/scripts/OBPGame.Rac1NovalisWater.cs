using OBP.Core;
using Godot;
using OBP.RAC1.Player;
using OBP.Runtime;

namespace OneBigPackage;

public partial class OBPGame
{
    private Rac1NovalisWaterSurfaces? _rac1NovalisWater;

    private void ConfigureRac1NovalisWater(RuntimeWorld world)
    {
        if (world.Game != ObpSourceGame.Rac1 || world.LevelId != 1) return;
        _rac1NovalisWater = Rac1NovalisWaterSurfaces.FromWorld(world);
        GD.Print($"[rac1-novalis-water] {_rac1NovalisWater.SurfaceTriangleCount} horizontal water-tagged source faces indexed");
    }

    private static bool AdmitRac1NovalisCollisionFace(
        int material, Vector3 a, Vector3 b, Vector3 c)
    {
        if (material is < 0 or > 255 ||
            Rac1CollisionFaceSemantics.Decode(material).SurfaceClass != 0)
            return true;
        Vector3 normal = (b - a).Cross(c - a);
        // Only the same horizontal water faces indexed by the source-owned
        // lookup become pass-through. Steep class-zero walls stay physical.
        return Mathf.Abs(normal.Y) < new Vector2(normal.X, normal.Z).Length();
    }

    private float? SampleRac1NovalisWaterHeight(Vector3 scenePosition) =>
        _rac1NovalisWater is { } water &&
        water.TrySample(-scenePosition.X, scenePosition.Z, out double height)
            ? (float)height
            : null;

    private void OnRac1NovalisDrowned()
    {
        if (_world is not { Game.SourceGame: ObpSourceGame.Rac1, LevelId: 1 } ||
            _player is null || _rac1Nanotech.Probe().IsDead)
            return;
        // Native oxygen/escape guards and action6a's sequence95 completion
        // precede this host checkpoint reset. Full retail death/camera/reset
        // orchestration remains outside the bounded water controller.
        var dead = _rac1Nanotech.ApplyEnvironmentalDeathReset();
        _player.Rac1GameplayState = dead;
        _rac1CombatStatus = "Novalis drowned (native water action completed)";
        RefreshRac1HudState();
        GD.Print($"[rac1-novalis-water] {_rac1CombatStatus}");
    }
}
