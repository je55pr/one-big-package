using Godot;
using OBP.Godot;
using OBP.Core;
using OBP.IO;
using OBP.RAC1.Camera;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

public partial class OBPGame
{
    /// <summary>Explicit staged lifecycle test; not an original-game parity or cross-world replay claim.</summary>
    private async Task RunPlayerSessionSmokeAsync()
    {
        var originalOptions = CameraSettingsStore.Current;
        try
        {
            Require(OrdinaryPlayerSession && _playerSession is not null && _playerNative is not null, "ordinary player owner required");
            var owner = _playerSession!;
            var native = _playerNative!;
            var controller = native.Weapons;
            var configuration = owner.Capture().Configuration;
            SelectWeaponKey(FusionWeaponSession.Lancer);
            native.Held.HoldWrench();
            native.Health.ApplyHostAridiaContactDamage();
            owner.SetPreference("camera/horizontal", originalOptions.Horizontal == Rac1CameraDirection.Normal ? "Inverted" : "Normal");
            var initial = owner.Capture();
            foreach (var id in new[] { "rac2:LEVEL1", "rac3:TABLE1", "obp:fusion-lab", "rac1:LEVEL0", "obp:fusion-lab", "rac1:LEVEL0" })
            {
                bool entered = id == "obp:fusion-lab" ? EnterFusionLab() : OnDestinationChosen(TrilogyWorldProviders.Resolve(id)!);
                Require(entered, "world entry " + id);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(ReferenceEquals(owner, _playerSession) && ReferenceEquals(controller, _fusion), "player/controller owner replaced");
                var current = owner.Capture();
                Require(current.Configuration == configuration && current.Generation == initial.Generation, "player profile/generation changed");
                Require(current.Loadout.SelectedWeapon == initial.Loadout.SelectedWeapon && current.Loadout.HandEquipment is null,
                    "selection or wrench hand ownership changed");
                Require(current.Health == initial.Health, "world entry reset player health");
                Require(current.Preferences.Count == initial.Preferences.Count && initial.Preferences.All(pair =>
                    current.Preferences.GetValueOrDefault(pair.Key) == pair.Value), "world entry reset player input/UI preferences");
                Require(_playerAvatarView is not null && _playerAvatar!.Identity == configuration.Avatar, "selected real avatar missing");
                Require(_playerRoot.GetChildCount() == 1 && _worldRoot.GetChildCount() == 1, "old scene/player survived teardown");
                Require(_fusionProjectiles.Count == 0 && _fusionAudioPlayers.Count == 0 && _fusionEffects.Count == 0, "old player effects/audio survived");
                Require(id != "obp:fusion-lab" || _activeDestination is null && _currentDestinationId == id, "Lab impersonates a retail destination");
                if (id == "obp:fusion-lab") await CheckPlayerSessionLabControlsAsync();
                GD.Print($"[player-session-smoke] {id}: same player/profile/selection/health; one player/scene; no old effects");
            }
            await CheckDeferredWorldEntryOwnershipAsync();
            await CheckHostOperationCancellationAsync();
            var liveWorld = _world;
            var livePlayer = _player;
            var avatarSource = _sources.Get(ObpSourceGame.Rac1)!;
            _sources.Remove(ObpSourceGame.Rac1);
            try
            {
                Require(!EnterFusionLab(), "missing required player source was accepted");
                Require(ReferenceEquals(liveWorld, _world) && ReferenceEquals(livePlayer, _player),
                    "missing avatar source destroyed the live player/world");
                Require(owner.Capture().Loadout.SelectedWeapon == initial.Loadout.SelectedWeapon, "source removal reset desired equipment");
            }
            finally
            {
                using var reader = new FileRandomAccessReader(avatarSource.Path);
                _sources.Restore(new(avatarSource.Game, avatarSource.Identity.BuildId, avatarSource.Path), reader);
                RefreshPlayerSourceAvailability();
            }
            Require(!EnterWorld("test:failed-preparation", () => throw new InvalidDataException("Intentional staged-load failure"), null, null),
                "invalid source was admitted");
            Require(ReferenceEquals(liveWorld, _world) && ReferenceEquals(livePlayer, _player) && ReferenceEquals(owner, _playerSession),
                "failed preparation destroyed the live world/player");
            bool ownedBlaster = native.Inventory.Owns(OBP.RAC1.Gameplay.Rac1WeaponId.Blaster);
            SetRac1WeaponPreview(false);
            Require(native.Inventory.Owns(OBP.RAC1.Gameplay.Rac1WeaponId.Blaster), "diagnostic preview did not bind to the player owner");
            SetRac1WeaponPreview(true);
            Require(native.Inventory.Owns(OBP.RAC1.Gameplay.Rac1WeaponId.Blaster) == ownedBlaster, "preview granted permanent ownership");
            owner.SetPreference("camera/horizontal", originalOptions.Horizontal.ToString());
            owner.Restore(initial);
            Require(CameraSettingsStore.Current.Horizontal.ToString() == initial.Preferences["camera/horizontal"], "restore did not apply host preferences");
            GD.Print("[player-session-smoke] PASS: persistent owner, native/custom round-trip, retained selections/health and failed-stage isolation");
            ApplicationLifecycle.RequestQuit(this, "player-session-smoke-pass", 0);
        }
        catch (Exception error)
        {
            GD.PrintErr("[player-session-smoke] FAIL: " + error);
            ApplicationLifecycle.RequestQuit(this, "player-session-smoke-fail", 3);
        }
        finally { CameraSettingsStore.Set(originalOptions, persist: false); }
    }

    private async Task CheckHostOperationCancellationAsync()
    {
        var pulse = new Node();
        try
        {
            bool emitting = false, resumedInsideSignal = false;
            async Task Probe()
            {
                await GodotAsyncWait.SignalAsync(pulse, Node.SignalName.TreeEntered, CancellationToken.None);
                resumedInsideSignal = emitting;
            }
            var probe = Probe();
            emitting = true;
            pulse.EmitSignal(Node.SignalName.TreeEntered);
            emitting = false;
            await probe;
            Require(resumedInsideSignal, "signal wait moved continuation into a different callback phase");
        }
        finally { pulse.Free(); }
        using var operations = new HostOperationScope();
        int subscriptions = RenderingServer.Singleton.GetSignalConnectionList(RenderingServer.SignalName.FramePostDraw).Count;
        operations.Start("waiting-for-render", token => GodotAsyncWait.SignalAsync(
            RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw, token), lifetime: CancellationToken.None);
        Require(RenderingServer.Singleton.GetSignalConnectionList(RenderingServer.SignalName.FramePostDraw).Count == subscriptions + 1,
            "owned signal wait was not connected");
        await operations.StopAsync();
        Require(operations.PendingCount == 0 && operations.TakeFailures().Count == 0,
            "canceled host work failed or did not drain");
        Require(RenderingServer.Singleton.GetSignalConnectionList(RenderingServer.SignalName.FramePostDraw).Count == subscriptions,
            "canceled render signal left a subscription");
        using var canceled = new CancellationTokenSource();
        bool metadataRead = false;
        string output = Path.Combine(ProjectSettings.GlobalizePath("res://../captures"), "canceled-" + Guid.NewGuid().ToString("N") + ".png");
        var capture = CaptureHarness.CaptureAsync(this, output, 60, () =>
        {
            metadataRead = true;
            return new Dictionary<string, object?>();
        }, canceled.Token);
        canceled.Cancel(); // Keep this Godot-thread cancellation synchronous while checking subscription cleanup.
        try { await capture; throw new InvalidOperationException("Canceled capture completed successfully."); }
        catch (OperationCanceledException error) when (error.CancellationToken == canceled.Token)
        { /* The deliberate cancellation must finish without publishing capture metadata. */ }
        Require(!metadataRead && !File.Exists(output), "canceled capture accessed a retired viewport or wrote an output");
        GD.Print("[player-session-smoke] PASS: canceled host/render waits drain, disconnect and publish no capture");
    }

    private async Task CheckDeferredWorldEntryOwnershipAsync()
    {
        var owner = _playerSession;
        bool staleEntryRan = false;
        WorldScenes.PrepareLater(async token =>
        {
            await WaitForWorldPreparationFrameAsync(token);
            staleEntryRan = true;
            EnterFusionLab();
        }, (_, _) => Require(false, "canceled deferred entry reported failure"));
        var pending = WorldScenes.PendingPreparation;
        ShowDestinationSelector();
        await pending;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(!staleEntryRan && _world is null && _mode == Mode.Selector,
            "deferred entry replaced newer navigation");
        Require(ReferenceEquals(owner, _playerSession), "canceling navigation replaced the player owner");
        Require(OnDestinationChosen(TrilogyWorldProviders.Resolve("rac1:LEVEL0")!), "entry after cancellation failed");
        GD.Print("[player-session-smoke] PASS: canceled deferred entry cannot replace newer navigation");
    }

    private async Task CheckPlayerSessionLabControlsAsync()
    {
        var selected = _fusion!.Selected;
        var hand = _rac1HeldItem.Hand.Equipment;
        foreach (var weapon in new[] { new SourceWeaponKey(ObpSourceGame.Rac1, 8), new(ObpSourceGame.Rac1, 10), FusionWeaponSession.Lancer, FusionWeaponSession.ShockCannon })
        {
            SelectWeaponKey(weapon);
            var loadout = _playerSession!.Capture().Loadout;
            Require(EnterFusionLab(), "Lab control trial entry failed");
            Require(_playerSession.Capture().Loadout.SelectedWeapon == weapon &&
                _playerSession.Capture().Loadout.HandEquipment == loadout.HandEquipment, "Lab control trial reset equipment");
            await CheckPlayerSessionLabMovementAsync(weapon);
        }
        SelectWeaponKey(selected);
        if (hand is null) _rac1HeldItem.HoldWrench();
    }

    private async Task CheckPlayerSessionLabMovementAsync(SourceWeaponKey weapon)
    {
        var player = _player!;
        var previous = player.PlaytestInputFilter;
        try
        {
            player.PlaytestInputFilter = _ => default;
            await PhysicsFramesAsync(30);
            var start = player.GlobalPosition;
            player.PlaytestInputFilter = _ => new RawPlayerInputFrame { Move = new(0, -1) };
            await PhysicsFramesAsync(90);
            float distance = new Vector2(player.GlobalPosition.X - start.X, player.GlobalPosition.Z - start.Z).Length();
            Require(distance > 1, "Lab entry left the persistent player unable to move");
            player.PlaytestInputFilter = _ => default;
            await PhysicsFramesAsync(20);
            float floor = player.GlobalPosition.Y, apex = floor;
            player.PlaytestInputFilter = _ => new RawPlayerInputFrame { JumpHeld = true };
            for (int i = 0; i < 30; i++)
            {
                await PhysicsFramesAsync(1);
                apex = Math.Max(apex, player.GlobalPosition.Y);
            }
            Require(apex > floor + .1f, "Lab entry left the persistent player unable to jump");
            GD.Print($"[player-session-smoke] Lab controls {weapon}: moved {distance:0.00}, jumped {apex - floor:0.00}");
        }
        finally { player.PlaytestInputFilter = previous; }
    }
}
