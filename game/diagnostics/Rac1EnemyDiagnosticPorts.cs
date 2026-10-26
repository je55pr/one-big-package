using System.Collections.Immutable;
using System.Numerics;
using OBP.Fusion;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.RAC1.Player;
using OBP.Core;

namespace OBP.Game.Diagnostics;

/// <summary>Copied facts about a source-qualified actor in one live world generation.</summary>
public sealed record Rac1EnemyDiagnosticSnapshot(ActorInstanceKey Key, Vector3 Position, Vector3 BodyPosition,
    Vector3 AuthoredHome, Vector3 Forward, Vector3 Socket, bool Visible, uint CollisionLayer, uint CollisionMask,
    bool Grounded, bool GodotFloor, bool NeedsGroundPlacement, bool Buried, bool DeathPresented, bool FlamePresent,
    RuntimeObjectAnimationSample? AnimationSample, int AnimationSequence, float AnimationPosition, bool AnimationBlending);
public sealed record Rac1EnemyPopulationSnapshot(int Enemies, int Class1440, int Peckbots, int Birdbots,
    long BirdbotShots, int Nanotech, int WorldSwitches, bool RetryVisible);
public sealed record Rac1Class1440DiagnosticSnapshot(float Health, int NativeState, int Sequence, int HitCooldown,
    bool HasAttackSlot, bool ReactionActive);
public sealed record Rac1PeckbotDiagnosticSnapshot(int NativeState, int Sequence, float Health, bool InitialLoiter);
public sealed record Rac1BirdbotDiagnosticSnapshot(Rac1NovalisBirdbotSession.HostMode Mode, int Sequence, float Health);
public sealed record Rac1ToadDiagnosticSnapshot(int NativeState, float Health, int? NativeSequence, int NativeSequenceUpdate,
    int? LinkedTargetInstanceIndex, float NavigationYawOffset, bool Active, bool Visible, bool ReactionActive,
    Rac1Class749AttackEvent? Attack);
public sealed record Rac1DiagnosticRayHit(Vector3 Position, Vector3 Normal, ActorInstanceKey? Actor = null);
public sealed record Rac1EnemyPerformanceSnapshot(double PhysicsMilliseconds, double ProcessMilliseconds);

public interface IRac1EnemyDiagnosticFacts
{
    Rac1EnemyPopulationSnapshot ReadPopulation();
    ImmutableArray<ActorInstanceKey> ReadEnemies();
    Rac1EnemyDiagnosticSnapshot ReadEnemy(ActorInstanceKey key);
    Rac1Class1440DiagnosticSnapshot ReadRobot(ActorInstanceKey key);
    Rac1PeckbotDiagnosticSnapshot ReadPeckbot(ActorInstanceKey key);
    Rac1BirdbotDiagnosticSnapshot ReadBirdbot(ActorInstanceKey key);
    Rac1ToadDiagnosticSnapshot ReadToad(ActorInstanceKey key);
    ImmutableArray<Vector3> ReadEntrancePath(ActorInstanceKey key);
    ImmutableArray<Vector3> ReadActivationRegion(ActorInstanceKey key);
    bool SupportsSequence(ActorInstanceKey key, int sequence);
    Rac1EnemyPerformanceSnapshot ReadPerformance();
}

/// <summary>Explicit encounter staging and contact queries, never an arbitrary host-field mutation API.</summary>
public interface IRac1EnemyDiagnosticActions
{
    IDisposable PreservePlayerPose();
    IDisposable PreserveActorPose(ActorInstanceKey actor);
    void StageRoot(ActorInstanceKey key, Vector3 position);
    void StageBody(ActorInstanceKey key, Vector3 position);
    void SetPlacementPending(ActorInstanceKey key, bool pending);
    void SetGrounded(ActorInstanceKey key, bool grounded);
    bool TryStrike(ActorInstanceKey key);
    bool TryGround(ActorInstanceKey key, float upwardReach, float downwardReach);
    float? MeasureGroundSeparation(ActorInstanceKey key);
    Rac1DiagnosticRayHit? Ray(Vector3 from, Vector3 to, ImmutableArray<ActorInstanceKey> excluded, bool excludePlayer);
    bool RayTouchesPlayer(ActorInstanceKey attacker, Vector3 from, Vector3 to);
}

public sealed record Rac1DiagnosticPickup(int Id, int NativeClassId, Vector3 NativePosition, int NativeState, float CollectionStep);
public sealed record Rac1DiagnosticLootSnapshot(int Wallet, int Outstanding, ImmutableArray<Rac1DiagnosticPickup> Pickups,
    ImmutableArray<int> DebrisClasses);
public interface IRac1DiagnosticLootFacts { Rac1DiagnosticLootSnapshot ReadLoot(); }
public interface IRac1EnemyDiagnosticCapture
{
    Task CaptureIfRequestedAsync(string name, Vector3 focus, CancellationToken cancellationToken);
}
public interface IRac1EncounterLifecycle
{
    Rac1RatchetNanotechSnapshot ReadNanotech();
    void ApplyAdmittedPlayerDamage(Rac1GameplayDamageEvent damage);
    void RetryLevel();
}
public interface IRac1WrenchApproachFixture
{
    double ControlYaw { get; }
    bool Admits(Vector3 targetCenter);
}

/// <summary>A stable identity with live immutable reads; it carries no scene node or native controller.</summary>
public sealed record Rac1DiagnosticEnemy(ActorInstanceKey Key, IRac1EnemyDiagnosticFacts Facts)
{
    public Rac1EnemyDiagnosticSnapshot Read() => Facts.ReadEnemy(Key);
}

/// <summary>Validates the existing actor identity against the observed world lease; it owns no actor registry.</summary>
public static class Rac1DiagnosticActorScope
{
    public static void RequireCurrent(ActorInstanceKey key, DiagnosticWorldSnapshot world)
    {
        if (string.IsNullOrWhiteSpace(world.WorldInstanceId) || key.WorldInstanceId != world.WorldInstanceId ||
            key.Generation != world.Generation || key.Target.SourceGame != ObpSourceGame.Rac1)
            throw new DiagnosticUnavailableException("The actor belongs to a retired or different source world.");
    }
}
