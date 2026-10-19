using Godot;
using OBP.RAC1.Player;
using OBP.Runtime;

namespace OneBigPackage;

public partial class OBPGame
{
    private Rac1NovalisWaterSurfaces? _rac1NovalisWater;

    private void ConfigureRac1NovalisWater(RuntimeWorld world)
    {
        if (world.Game != "rac1" || world.LevelId != 1) return;
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
        if (_world is not { Game: "rac1", LevelId: 1 } ||
            _player is null || _rac1Nanotech.Probe().IsDead)
            return;
        // Host-owned drowning admission: oxygen exhaustion plus submerged
        // placement. Native source action 0x6a was recovered, but the complete
        // retail drowning predicate and exact presentation remain outstanding.
        // The existing environmental checkpoint reset is reused as a clearly
        // marked OBP fallback rather than claiming native Novalis reset parity.
        var dead = _rac1Nanotech.ApplyEnvironmentalDeathReset();
        _player.Rac1GameplayState = dead;
        _rac1CombatStatus = "Novalis drowned (OBP host oxygen/depth admission)";
        RefreshRac1HudState();
        GD.Print($"[rac1-novalis-water] {_rac1CombatStatus}");
    }
}
