using System.Collections.Immutable;
using System.Numerics;
using OBP.Fusion;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Progression;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OBP.Game.Diagnostics;

public sealed record Rac1DiagnosticObjectSnapshot(ActorInstanceKey Key, Vector3 Position, Vector3 Forward,
    bool Visible, bool GameplayActive, bool RecoveredVeldinPlacement);
public sealed record Rac1DiagnosticObject(ActorInstanceKey Key, IRac1CombatDiagnosticFixture Facts)
{
    public Rac1DiagnosticObjectSnapshot Read() => Facts.ReadObject(Key);
}
public sealed record Rac1CombatOpeningDiagnostic(bool PersistenceRequested, bool PersistenceEnabled,
    Rac1CampaignRestoreKind RestoreKind, int CurrentLevel, int AdmittedDestinations, bool OwnsFirstRanged);
public sealed record Rac1CombatPopulationDiagnostic(int AuthoredClass749, int PresentedClass749, int HostedClass749, int RegisteredClass749);
public sealed record Rac1CombatPresentationDiagnostic(HudWeapon? Weapon, bool WrenchPresent, bool WrenchVisible, bool WrenchParentedToAvatar);
public sealed record Rac1CombatPickupDiagnostic(int Id, bool Available, Vector3 Position, bool CrateOwned);
public sealed record Rac1CombatLootDiagnostic(int DestroyedCrates, int CollectedBolts, ImmutableArray<Rac1CombatPickupDiagnostic> Pickups);
public sealed record Rac1BombContactDiagnostic(long ProjectileId, ImmutableArray<Rac1BombGloveDamageResult> DamageResults)
{
    public int AdmittedContactCount => DamageResults.Length;
}
public sealed record Rac1BombDiagnostic(bool FireRequested, ImmutableArray<long> Projectiles, Rac1BombContactDiagnostic? LastContact);
public sealed record Rac1BombWitnessDiagnostic(long ProjectileId, SourceWeaponKey Weapon, Vector3 NativePosition, int NativeState);
public interface IRac1BombDiagnosticWitness { Rac1BombWitnessDiagnostic Read(); }
public interface IRac1EncounterMenuChecks
{
    Task CheckVeldinPauseAsync(CancellationToken token);
    Task CheckRetailNavigationAsync(string world, CancellationToken token);
}
public sealed record Rac1CombatScenarioPorts(IRac1EnemyDiagnosticFacts Enemies, IRac1EnemyDiagnosticActions Actors,
    IRac1CombatDiagnosticFixture Combat, IRac1WrenchApproachFixture Approach, IDiagnosticControllerEquipment Equipment,
    IDiagnosticNavigationActions Navigation, IRac1EncounterMenuChecks Menus, IDiagnosticEnvironmentalDeath Environment,
    IRac1EncounterLifecycle Lifecycle);
public sealed record Rac1PlayerCameraDiagnostic(bool ActiveRecovered, string ControllerLabel, bool RuntimeStatePresent,
    Vector3 Eye, double ControlYaw, double ManualPitch);

/// <summary>Focused synthetic combat and ordinary Veldin observations/actions. No host, engine node or native controller escapes.</summary>
public interface IRac1CombatDiagnosticFixture
{
    Rac1CombatOpeningDiagnostic ReadOpening();
    Rac1CombatPopulationDiagnostic ReadPopulation();
    Rac1CombatPresentationDiagnostic ReadPresentation();
    Rac1CombatLootDiagnostic ReadLoot();
    Rac1BombDiagnostic ReadBombs();
    IRac1BombDiagnosticWitness? CaptureBombWitness();
    ImmutableArray<ActorInstanceKey> ReadHostiles();
    ImmutableArray<ActorInstanceKey> ReadCrates();
    bool IsAvailable(ActorInstanceKey actor);
    Rac1DiagnosticObjectSnapshot ReadObject(ActorInstanceKey actor);
    void StageObject(ActorInstanceKey actor, Vector3 position);
    void SetPresence(ActorInstanceKey actor, RuntimeEntityPresence presence);
    void PlacePlayerFacing(Vector3 position, Vector3 direction, bool grounded);
    void StagePlayerPosition(Vector3 position, bool clearVelocity);
    Rac1PlayerCameraDiagnostic ReadPlayerCamera();
    ImmutableArray<Vector3> ReadPlayerContactNormals();
    ImmutableArray<string> ReadPlayerContactDescriptions();
    void RequestPrimaryAttack();
    IDisposable SubscribeDamage(Action<Rac1GameplayDamageDispatch> observer);
}
