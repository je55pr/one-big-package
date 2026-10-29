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
    Action<int, int> Hurt, Action<HudFeedbackDraft[]> Hud);

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

internal delegate void Rac1FrogEnemyConfiguration(RuntimeWorld world, RuntimeWorldScene.Result scene,
    IReadOnlyList<NVector3[]> paths, IReadOnlyList<Rac1ClipVolume> volumes, List<RuntimeWorldScene.DynamicObjectNode> nodes);
internal delegate void Rac1QueenEnemyConfiguration(RuntimeWorld world, RuntimeWorldScene.Result scene,
    IReadOnlyList<int[]> groups, Rac1StaticClasses.MobyClass cls, ReadOnlySpan<byte> payload,
    Rac1AlienQueenGateNative tuning, NVector3 camera);
internal delegate void Rac1WaveEnemyConfiguration(RuntimeWorld world, RuntimeWorldScene.Result scene,
    IReadOnlyList<int[]> groups, IReadOnlyList<Rac1ClipVolume> volumes, Rac1StaticClasses.MobyClass cls, ReadOnlySpan<byte> payload);

// Temporary integration ports to #362/#363/#368's existing source owners.
// They contain no root reference, shared AI, inventory or player-selection policy.
internal sealed record Rac1EnemyNeighborPorts(Func<bool> Current,
    Action EndQueenCamera, Action ResetCarriers, Action ResetQueenChildren, Action ClearFrogState,
    Action ResetAridia, Action ResetAridiaScripts, Action ResetConstructorbots,
    Func<Rac1AridiaEnemySession> Aridia, Action<IReadOnlyList<Rac1ClipVolume>> AridiaVolumes,
    IDictionary<int, Rac1AridiaConstructorbotNative.Frame> Constructorbots, IDictionary<int, Vector3> ConstructorbotWaypoints,
    Action<RuntimeWorld, RuntimeWorldScene.Result, IReadOnlyList<Rac1Instances.MobyInstance>> AridiaScripts,
    Rac1FrogEnemyConfiguration Frogs, Rac1QueenEnemyConfiguration Queen, Rac1WaveEnemyConfiguration Waves,
    Action<RuntimeWorldScene.Result> Carriers,
    Func<Rac1FrogSession?> FrogSession,
    Action TickShuttle, Action TickWaves, Action TickQueenFlight, Action TickQueenRecovery, Action TickQueenChildren,
    Func<int, bool> CarrierChild, Action<Rac1HostedEnemy> TickCarrier,
    Action<Rac1HostedEnemy> TickAridia, Action<Rac1HostedEnemy> TickFrog,
    Action<Rac1HostedEnemy> HideAridia, Action<Rac1HostedEnemy> ShowAridia,
    Func<RuntimeWorldScene.DynamicObjectNode, Rac1GameplayDamageEvent, bool> AridiaDamage);

internal sealed record Rac1EnemyPorts(Func<string> SourcePath, Func<Rac1PickupRandom> Random,
    Rac1EnemyPlayerPorts Player, Rac1EnemyCombatPorts Combat, Rac1EnemyNeighborPorts Neighbors);

internal static class Rac1EnemyCoordinates
{
    internal static NVector3 ToEffectNative(Vector3 point) => new(-point.X, point.Z, point.Y);
    internal static Vector3 FromEffectNative(NVector3 point) => new(-point.X, point.Z, point.Y);
}
