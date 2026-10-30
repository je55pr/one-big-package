using System.Collections.Immutable;
using System.Numerics;
using OBP.Core;
using OBP.Fusion;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Game.Diagnostics;

public sealed record Rac1LootMotionDiagnostic(int Id, int NativeClass, int NativeState, Vector3 NativePosition, Vector3 WorldPosition);
public sealed record Rac1AmmoPickupDiagnostic(SourceWeaponKey Item, int Amount, Vector3 Position, Vector3 Velocity, int DelayTicks, int CurrentAmmo);
public sealed record Rac1LootDiagnosticState(ObpSourceGame Source, int Wallet, int? HudWallet, int Sounds, int Health,
    int BombAmmo, int OutstandingCratePickups, ImmutableArray<int> DebrisClasses,
    ImmutableArray<Rac1LootMotionDiagnostic> Pickups, ImmutableArray<Rac1AmmoPickupDiagnostic> Ammo,
    ImmutableArray<bool> HealthCloudAttraction);
public sealed record Rac1PlacedBoltPlacement(ActorInstanceKey Actor, int PickupId, int NativeClass,
    bool Active, bool PreserveAuthoredHeight, Vector3 NativeAuthoredPosition, float? MotionNativeZ, bool OriginalNodeAlias);
public sealed record Rac1PlacedBoltDiagnostic(int PickupId, int NativeState, bool Collected, bool Active,
    Vector3 Position, Quaternion Rotation, Vector3 Scale, bool Visible, RuntimeEntityPresence Presence, bool PickupNodePresent);
public interface IRac1PlacedBoltDiagnosticWitness { Rac1PlacedBoltDiagnostic Read(); }
public interface IRac1LootDiagnosticFixture
{
    Rac1LootDiagnosticState Read();
    bool OriginalPickupMeshes { get; }
    bool PlacedBoltPresentationIntact { get; }
    int PendingPlacedBolts { get; }
    ImmutableArray<Rac1PlacedBoltPlacement> ReadPlacedBolts();
    IRac1PlacedBoltDiagnosticWitness CapturePlacedBolt(ActorInstanceKey actor);
    void StagePlacedBolt(ActorInstanceKey actor, Vector3 floor);
    bool CrateHasBody(ActorInstanceKey actor);
    bool StrikeCrate(ActorInstanceKey actor);
    void InjectRetainedClass749Damage();
}
public interface IRac1LootDiagnosticCapture { Task CaptureAsync(string name, Vector3 focus, CancellationToken token); }
public sealed record Rac1BreakableDiagnosticEntry(ActorInstanceKey Actor, int? NativeState,
    RuntimeEntityPresence Presence, bool HasCollider);
public sealed record Rac1BreakableDiagnosticState(ImmutableArray<Rac1BreakableDiagnosticEntry> Explosives,
    ImmutableArray<Rac1BreakableDiagnosticEntry> Novalis, int NovalisDestroyed);

/// <summary>Deliberately bounded actions, available only through an explicit crate scenario.</summary>
public interface IRac1BreakableDiagnosticFixture
{
    Rac1BreakableDiagnosticState Read();
    bool TouchExplosive(ActorInstanceKey actor);
    bool BreakNovalis(ActorInstanceKey actor, Rac1NovalisBreakableSession.ContactKind contact);
}

public sealed record Rac1LootScenarioPorts(IRac1LootDiagnosticFixture Loot, IRac1WeaponTargetFixture Targets,
    IDiagnosticControllerEquipment Equipment, IRac1LootDiagnosticCapture Capture,
    IRac1BreakableDiagnosticFixture? Breakables = null);
