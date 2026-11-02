using Godot;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>Resolved Godot-space player entry; no player session or scene lease is owned here.</summary>
internal readonly record struct GodotSelectedPlayerSpawnPose(Vector3 Position, float Yaw);

/// <summary>
/// Chooses authored, diagnostic and bounds fallback placements in the original order.
/// Scene registration and player lifecycle remain with the application composition root.
/// </summary>
internal static class GodotPlayerSpawnPlacement
{
    internal static GodotSelectedPlayerSpawnPose Resolve(RuntimeWorld world,
        RuntimeSpawn? stagedStart, bool animFocus, bool scripted,
        Func<GodotSelectedPlayerSpawnPose?> crateFocus, Action<string> log)
    {
        var b = world.Bounds;
        var preferredStart = stagedStart ?? world.PreferredPlayerStart;
        bool explicitPlayerStart = stagedStart is not null || world.PlayerStart is not null;
        bool nativeStartUsable = preferredStart is { } s
            && (explicitPlayerStart
                || (s.X > b.Min.X && s.X < b.Max.X && s.Z > b.Min.Z && s.Z < b.Max.Z
                    && s.Y > b.Min.Y - 4 && s.Y < b.Max.Y + 40));

        Vector3 spawn;
        float yaw = 0f;
        if (nativeStartUsable && preferredStart is { } sp)
        {
            RuntimeSpawnScenePose pose = RuntimeSpawnSceneAdapter.ToScenePose(sp);
            spawn = pose.Position;
            yaw = pose.SceneYaw;
            string source = stagedStart is not null
                ? "staged source/authored entry"
                : explicitPlayerStart ? "explicit player start" : "native ship point";
            log($"[OBPGame] spawn from {source} ({sp.X:0},{sp.Y:0},{sp.Z:0})");
        }
        else
        {
            // ship-less levels (Aranos, the Aranos return): drop at the top
            // centre of the world bounds and let the capsule fall to collision.
            spawn = RuntimeWorldScene.ToScene(
                (b.Min.X + b.Max.X) * 0.5,
                b.Max.Y + 6.0,
                (b.Min.Z + b.Max.Z) * 0.5);
            log("[OBPGame] no in-bounds ship point — spawning at world-bounds centre");
        }

        // MobySequence showcase: stand right next to the first animated moby.
        if (animFocus && world.AnimatedMeshes is { Count: > 0 } anim)
        {
            var f0 = anim[0].Frames[0];
            double cx = 0, cy = 0, cz = 0;
            int n = f0.Length / 3;
            for (int i = 0; i < f0.Length; i += 3) { cx += f0[i]; cy += f0[i + 1]; cz += f0[i + 2]; }
            var centre = RuntimeWorldScene.ToScene(cx / n, cy / n, cz / n);
            spawn = centre + new Vector3(0f, 2.5f, 9f);
            var look = centre - spawn;
            yaw = Mathf.Atan2(look.X, look.Z) + Mathf.Pi;
            log($"[OBPGame] anim-focus spawn near {anim[0].Name} at ({cx / n:0},{cy / n:0},{cz / n:0})");
        }

        if (crateFocus() is { } focus)
        {
            spawn = focus.Position;
            yaw = focus.Yaw;
            log($"[crate-debug] focus spawn at {spawn}");
        }

        // When no native player/ship start is usable, deterministic captures
        // face the world centre because the bounds fallback has no authored heading.
        if (scripted && !nativeStartUsable)
        {
            var centre = RuntimeWorldScene.ToScene(
                (b.Min.X + b.Max.X) * 0.5, spawn.Y, (b.Min.Z + b.Max.Z) * 0.5);
            var toCentre = centre - spawn;
            if (toCentre.LengthSquared() > 1f)
            {
                yaw = Mathf.Atan2(toCentre.X, toCentre.Z) + Mathf.Pi;
            }
        }

        return new(spawn, yaw);
    }
}
