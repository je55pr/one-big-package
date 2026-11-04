using System.Collections.Immutable;
using System.Numerics;
using OBP.Core;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;

namespace OBP.Game.Diagnostics;

public sealed record DiagnosticSessionState(PlayerSessionConfiguration Configuration, long Generation,
    PlayerHealthSnapshot Health, SourceWeaponKey? SelectedWeapon, SourceWeaponKey? HandEquipment,
    ImmutableArray<SourceWeaponKey?> QuickSelect, ImmutableDictionary<string, string> Preferences,
    ImmutableDictionary<string, long> Currencies);
public sealed record DiagnosticPlayerSessionObservation(bool Ordinary, bool NativePresent, Guid SessionOwner,
    Guid NativeController, Guid LiveController, ulong? PlayerNode, string? WorldInstance,
    DiagnosticSessionState? State, bool AvatarPresented, PlayerAvatarIdentity? PresentedAvatar,
    int PlayerChildren, int WorldChildren, int Projectiles, int AudioPlayers, int Effects,
    bool RetailDestinationActive, string? Destination, bool NativeBlasterOwned, bool NativeProfile, bool RecoveredCamera);
public interface IDiagnosticPlayerRestorePoint
{
    DiagnosticSessionState State { get; }
    void Restore();
}
public interface IDiagnosticCameraSettingsLease : IDisposable { string Horizontal { get; } }

/// <summary>Explicit fixture setup on the one persistent player. No native controller or inventory escapes.</summary>
public interface IDiagnosticPersistentPlayerFixture
{
    DiagnosticPlayerSessionObservation Read();
    IDiagnosticPlayerRestorePoint CaptureRestorePoint();
    IDiagnosticCameraSettingsLease PreserveCameraSettings();
    string ReadCameraHorizontal();
    void SetPreference(string key, string value);
    void SelectWeapon(SourceWeaponKey weapon);
    void HoldWrench();
    void ApplyProvisionalContactDamage();
    void SetEquipmentPreview(bool disable);
}

public sealed record DiagnosticPlayerInputFrame(Vector2 Move = default, Vector2 CameraIntent = default,
    bool JumpHeld = false, bool CrouchHeld = false, bool ActionJustPressed = false,
    bool TraversalHeld = false, bool EquippedUseJustPressed = false, bool ActionHeld = false);
public interface IDiagnosticPlayerInputFixture { IDiagnosticPlayerInputLease Acquire(); }
public interface IDiagnosticPlayerInputLease : IDisposable { void SetFilter(Func<int, DiagnosticPlayerInputFrame> input); }
public interface IDiagnosticSourceAttachmentFixture { IDisposable Detach(ObpSourceGame game, bool refreshOnDetach); }
public interface IDiagnosticDeferredNavigation : IAsyncDisposable
{
    Task Completion { get; }
    bool EntryRan { get; }
    bool FailureReported { get; }
}
public interface IDiagnosticWorldLifecycleFixture
{
    IDiagnosticDeferredNavigation ScheduleLabAfterNextFrame(CancellationToken lifetime);
    bool TryFailedPreparation();
}
public interface IDiagnosticSessionEngineChecks { Task RunAsync(CancellationToken cancellationToken); }
