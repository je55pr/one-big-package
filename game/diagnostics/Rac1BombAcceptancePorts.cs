using System.Collections.Immutable;
using System.Numerics;
using OBP.Runtime.Gameplay;
using OBP.Fusion;

namespace OBP.Game.Diagnostics;

public sealed record Rac1BombViewDiagnostic(bool GloveVisible, bool StagedVisible, bool WrenchVisible,
    int VertexCount, int TriangleCount, Vector3 Socket, Vector3 StagedPosition, bool StagedFollowsSocket);
public sealed record Rac1BombEffectsDiagnostic(int Fragments, int Bursts, int Particles, int Impacts, ImmutableArray<int> ParticleTypes);
public sealed record Rac1BombAcceptanceShot(long ProjectileId, SourceWeaponKey Weapon, Vector3 NativePosition,
    Vector3 NativeVelocity, int NativeState, bool Flying, bool OriginalModel);
public interface IRac1BombAcceptanceWitness { Rac1BombAcceptanceShot Read(); }
public interface IRac1BombAcceptanceFixture
{
    Rac1BombViewDiagnostic ReadView();
    Rac1BombEffectsDiagnostic ReadEffects();
    IRac1BombAcceptanceWitness CaptureSingleShot();
    bool HasLineOfSight(Vector3 from, Vector3 to);
    Vector3? ProbeWorldContact(Vector3 from, Vector3 to);
    string DescribeRay(Vector3 from, Vector3 to);
    bool ConsumeAmmo(int amount);
    string? SavedReproductionPath { get; }
}
public sealed record WeaponMovementStateDiagnostic(bool WorldPresent, int CapturedMobies, bool WeaponPreview, ImmutableArray<WeaponTargetKey> Morphed);
public interface IWeaponMovementStateFixture
{
    bool Loading { get; }
    WeaponMovementStateDiagnostic CaptureBoundary();
    /// <summary>Transfers cleanup ownership for the explicitly reloaded fixture world to the caller.</summary>
    Task<IDisposable> SaveAndReloadAsync(string fileName, CancellationToken token);
    Task<IDisposable> LoadAsync(string path, CancellationToken token);
}
