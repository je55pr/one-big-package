using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;

namespace OBP.Game.Diagnostics;

internal sealed record GodotPlayerSessionBindings(Func<bool> Ordinary, Func<ObpPlayerSession?> Session,
    Func<PlayerSessionNativeState?> Native, Func<FusionWeaponSession?> Controller, Func<PlayerHost?> Player,
    Func<Node?> AvatarView, Func<PlayerAvatarIdentity?> Avatar, Node PlayerRoot, Node WorldRoot,
    Func<(int Projectiles, int AudioPlayers, int Effects)> Transients, Func<bool> RetailDestination, Func<string?> Destination);

/// <summary>Copies persistent-player and presentation facts. State restore points keep their native payload private.</summary>
internal sealed class GodotPersistentPlayerFixture(GodotPlayerSessionBindings bindings, IDiagnosticWorldObservations worlds,
    Action<SourceWeaponKey> select, Action<bool> preview) : IDiagnosticPersistentPlayerFixture
{
    private static readonly ConditionalWeakTable<object, ReferenceId> Ids = new();
    private sealed record ReferenceId(Guid Value);
    private static Guid Identity(object? value) => value is null ? Guid.Empty : Ids.GetValue(value, _ => new(Guid.NewGuid())).Value;
    private ObpPlayerSession Session => bindings.Session() ?? throw new DiagnosticUnavailableException("No persistent player is bound.");
    private PlayerSessionNativeState Native => bindings.Native() ?? throw new DiagnosticUnavailableException("No native player adapter is bound.");
    public DiagnosticPlayerSessionObservation Read()
    {
        var session = bindings.Session(); var native = bindings.Native();
        var player = bindings.Player(); var avatar = bindings.AvatarView(); var transients = bindings.Transients();
        return new(bindings.Ordinary(), native is not null, Identity(session), Identity(native?.Weapons), Identity(bindings.Controller()),
            player is not null && GodotObject.IsInstanceValid(player) ? player.GetInstanceId() : null, worlds.ReadWorld().WorldInstanceId,
            session is null ? null : Freeze(session.Capture()), avatar is not null && GodotObject.IsInstanceValid(avatar), bindings.Avatar(),
            bindings.PlayerRoot.GetChildCount(), bindings.WorldRoot.GetChildCount(), transients.Projectiles, transients.AudioPlayers, transients.Effects,
            bindings.RetailDestination(), bindings.Destination(), native?.Inventory.Owns(Rac1WeaponId.Blaster) == true,
            player?.ControlProfile == PlayerHostProfiles.Native, player?.HasActiveRecoveredCamera == true);
    }
    internal static DiagnosticSessionState Freeze(PlayerSessionSnapshot state) => new(state.Configuration, state.Generation, state.Health,
        state.Loadout.SelectedWeapon, state.Loadout.HandEquipment, [.. state.Loadout.QuickSelect],
        state.Preferences.ToImmutableDictionary(StringComparer.Ordinal), state.Currencies.ToImmutableDictionary(StringComparer.Ordinal));
    public IDiagnosticPlayerRestorePoint CaptureRestorePoint() => new RestorePoint(Session, Session.Capture());
    public IDiagnosticCameraSettingsLease PreserveCameraSettings() => new CameraLease();
    public string ReadCameraHorizontal() => CameraSettingsStore.Current.Horizontal.ToString();
    public void SetPreference(string key, string value) => Session.SetPreference(key, value);
    public void SelectWeapon(SourceWeaponKey weapon) => select(weapon);
    public void HoldWrench() => Native.Held.HoldWrench();
    public void ApplyProvisionalContactDamage() => Native.Health.ApplyHostAridiaContactDamage();
    public void SetEquipmentPreview(bool disable) => preview(disable);
    private sealed class RestorePoint(ObpPlayerSession owner, PlayerSessionSnapshot state) : IDiagnosticPlayerRestorePoint
    {
        public DiagnosticSessionState State { get; } = Freeze(state);
        public void Restore() => owner.Restore(state);
    }
    private sealed class CameraLease : IDiagnosticCameraSettingsLease
    {
        private readonly OBP.RAC1.Camera.Rac1CameraOptions _original = CameraSettingsStore.Current;
        public string Horizontal => _original.Horizontal.ToString();
        public void Dispose() => CameraSettingsStore.Set(_original, persist: false);
    }
}

internal sealed class GodotPlayerInputFixture(Func<PlayerHost?> player) : IDiagnosticPlayerInputFixture
{
    public IDiagnosticPlayerInputLease Acquire() => new FilterLease(player() ?? throw new DiagnosticUnavailableException("The input fixture needs a live player."));
    private sealed class FilterLease(PlayerHost actor) : IDiagnosticPlayerInputLease
    {
        private readonly Func<RawPlayerInputFrame, RawPlayerInputFrame>? _previous = actor.PlaytestInputFilter;
        private bool _disposed;
        public void SetFilter(Func<int, DiagnosticPlayerInputFrame> input)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int tick = 0;
            actor.PlaytestInputFilter = _ =>
            {
                var frame = input(tick++);
                return new(new(frame.Move.X, frame.Move.Y), new(frame.CameraIntent.X, frame.CameraIntent.Y),
                    frame.JumpHeld, frame.CrouchHeld, frame.ActionJustPressed, null, frame.TraversalHeld, frame.EquippedUseJustPressed, frame.ActionHeld);
            };
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            if (GodotObject.IsInstanceValid(actor)) actor.PlaytestInputFilter = _previous;
        }
    }
}
