using System.Collections.Immutable;
using System.Numerics;
using OBP.Runtime.Gameplay;

namespace OBP.Game.Diagnostics;

public sealed record DiagnosticWeaponTransform(Vector3 Origin, Vector3 X, Vector3 Y, Vector3 Z);
public sealed record FusionWeaponDiagnosticStatus(bool Present, bool AssetsPresent, int Problems, bool Preparing, string? Failure,
    long Frame, bool Isolated, string? Loadout, bool WaitingForEntry, bool PlaytestPresent);
public sealed record FusionWeaponDamageDiagnostic(SourceWeaponKey Weapon, float SourceDamage, string Kind);
public sealed record FusionWeaponSessionDiagnostic(SourceWeaponKey? Selected, SourceWeaponKey? Held, bool WrenchHeld,
    int LancerAmmo, int CannonAmmo, int AcceptedHits, int AudioStarted, bool BeamVisible, int BeamUpdatesRemaining,
    FusionWeaponDamageDiagnostic? LastDamage, bool WrenchActive, bool NativeInventoryAlias, bool CampaignInventoryAlias);
public sealed record FusionWeaponTransientDiagnostic(int Projectiles, int LancerShots, int Effects, int PendingAudio, int AudioPlayers);
public sealed record FusionWeaponMenuDiagnostic(bool Open, ImmutableArray<SourceWeaponKey?> Entries, SourceWeaponKey? Selected);
public sealed record FusionWeaponHeldDiagnostic(SourceWeaponKey Weapon, bool Visible, bool HasMeshes,
    DiagnosticWeaponTransform Local, DiagnosticWeaponTransform Global, DiagnosticWeaponTransform? ExpectedHand);
public sealed record FusionWeaponAnimationDiagnostic(int? UseSequence, double? UseWeight, bool Blending, double BlendFraction,
    int? TraversalSequence, ImmutableArray<double>? Skin);
public interface IFusionAssetDiagnosticWitness { bool IsCurrent { get; } }
public interface IFusionPreparationDiagnosticWitness
{
    bool IsCurrent { get; }
    Task RequireCanceledAsync(CancellationToken token);
}
public interface IFusionSourceIsolation : IDisposable { void DetachAll(); void RestoreAll(); }
public interface IFusionWeaponDiagnosticFixture
{
    FusionWeaponDiagnosticStatus ReadStatus();
    FusionWeaponSessionDiagnostic ReadSession();
    FusionWeaponTransientDiagnostic ReadTransients();
    FusionWeaponMenuDiagnostic ReadMenu();
    FusionWeaponAnimationDiagnostic ReadAnimation();
    FusionWeaponHeldDiagnostic ReadHeld(SourceWeaponKey weapon);
    int HeldModelCount { get; }
    int ReadAmmo(SourceWeaponKey weapon);
    string OriginalInventory();
    string CurrentInventory();
    IFusionAssetDiagnosticWitness CaptureAssets();
    IFusionPreparationDiagnosticWitness CapturePreparation();
    IDisposable UseUnavailableAssets();
    IFusionSourceIsolation IsolateSources();
    void PrepareSources();
    void ResetWorldWeapons();
    void ClearTransients();
    bool ReenterWithoutIsolatedLoadout();
    void ShowDestinations();
    Task CaptureAsync(string name, bool profile, CancellationToken token);
}
