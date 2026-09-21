using Godot;
using OBP.Runtime;

namespace OBP.Godot.Player;

public sealed record RuntimeSpawnScenePose(Vector3 Position, float SceneYaw);

/// <summary>
/// Host-only placement adapter for an engine-neutral <see cref="RuntimeSpawn"/>.
/// The +3 unit lift is Godot collision/grounding clearance, not native game state.
/// </summary>
public static class RuntimeSpawnSceneAdapter
{
    public const double GroundingLift = 3d;

    public static RuntimeSpawnScenePose ToScenePose(RuntimeSpawn spawn)
    {
        ArgumentNullException.ThrowIfNull(spawn);
        return new RuntimeSpawnScenePose(
            RuntimeWorldScene.ToScene(spawn.X, spawn.Y + GroundingLift, spawn.Z),
            RuntimeWorldScene.ToSceneYaw(spawn.Yaw));
    }
}
