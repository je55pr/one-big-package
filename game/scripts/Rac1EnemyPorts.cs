using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

internal sealed record Rac1EnemyPlayerPorts(Func<PlayerHost?> Player, Func<bool> Alive, Func<bool> Persistent,
    Func<Rac1RatchetNanotechSession> Health,
    Action<RuntimeWorldScene.DynamicObjectNode, uint, int, ulong> SourceDamage,
    Action<int, int> Hurt, Action<HudFeedbackDraft[]> Hud,
    Action<RuntimeWorldScene.DynamicObjectNode, uint, int, ulong> AridiaSourceDamage);

internal sealed record Rac1EnemyCombatPorts(Func<Rac1HostedEnemy, Vector3, Vector3> Goal,
    Func<Rac1HostedEnemy, bool> HitsDecoy, Func<Rac1HostedEnemy, bool> ChargeDecoy,
    Func<int, Vector3?> MineOrigin, Func<int, Vector3?> DoomOrigin,
    Func<int, int> Reward, Action<Vector3, IReadOnlyList<Rac1BoltPickup>> Pickups,
    Action<RuntimeWorldScene.DynamicObjectNode, IEnumerable<int>> TrackPickups,
    Func<int, bool> HasFragment, Action<int, Rac1CrateDebris> Fragment,
    Action<int, int, Vector3> Sound, Func<RuntimeWorldScene.DynamicObjectNode, int, bool> ActorSound,
    Func<Vector3, Vector3, bool> LineOfSight, Action<Vector3> Explosion,
    Action<Vector3> FatalBurst, Func<RuntimeWorldScene.DynamicObjectNode, Rac1GameplayDamageEvent, bool> ExplosiveDamage,
    Action<RuntimeWorldScene.DynamicObjectNode, Rac1BoltCrateDamageAdmission> CrateDamage,
    Action<int, RuntimeEntityPresence> CratePresence, Func<int, bool> Chicken);

// The world lease check does not expose Aridia native state.
internal sealed record Rac1EnemyNeighborPorts(Func<bool> Current);

internal sealed record Rac1EnemyPorts(Func<string> SourcePath, Func<Rac1PickupRandom> Random,
    Rac1EnemyPlayerPorts Player, Rac1EnemyCombatPorts Combat, Rac1EnemyNeighborPorts Neighbors,
    Rac1FrogEncounterPorts Frogs);

/// <summary>
/// Non-owning engine integration for one R&C1 frog encounter lease. Camera,
/// effect and contact callbacks have no access to the native family internals.
/// </summary>
internal sealed record Rac1FrogEncounterPorts(
    Func<Viewport> Viewport,
    Func<Camera3D?> ActiveCamera,
    Action<Camera3D> SetActiveCamera,
    Func<Camera3D> FallbackCamera,
    Func<Camera3D?> GuidedCamera,
    Func<Rac1MobyRuntimeKey, bool> HasWrenchContact,
    Action<Rac1MobyRuntimeKey> AddWrenchContact,
    Func<int, string, Node3D> CreateFragmentNode,
    Action<Node3D, Rac1CrateDebris> TrackFragment,
    Func<System.Numerics.Quaternion, Quaternion> FromEffectOrientation);

internal static class Rac1EnemyCoordinates
{
    internal static NVector3 ToEffectNative(Vector3 point) => new(-point.X, point.Z, point.Y);
    internal static Vector3 FromEffectNative(NVector3 point) => new(-point.X, point.Z, point.Y);
}