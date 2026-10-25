using Godot;
using OBP.Godot;
using System.Text.Json;
using OBP.Fusion;

namespace OBP.Game;

public partial class OBPGame
{
    private async Task RunReconciliationRecordingSmokeAsync()
    {
        try
        {
            while (_playtest!.WaitingForEntry) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var configuration = _playerSession!.Capture().Configuration;
            int visit = 0;
            foreach (string destination in new[] { "rac1:LEVEL0", "rac2:LEVEL1", "rac3:TABLE1", "obp:fusion-lab", "rac1:LEVEL0" })
            {
                if (_currentDestinationId != destination)
                {
                    string before = JsonSerializer.Serialize(ProfilePlayer.Capture(_playerSession.Capture()) with { Generation = 0 });
                    Require(destination == "obp:fusion-lab" ? EnterFusionLab() : OnDestinationChosen(TrilogyWorldProviders.Resolve(destination)!), "capture world entry failed");
                    AcceptanceAssert(before == JsonSerializer.Serialize(ProfilePlayer.Capture(_playerSession.Capture()) with { Generation = 0 }), "transition-preserves-session", "player state unchanged at world commit");
                    while (_playtest.PreparingWorld) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                AcceptanceAssert(_playerSession.Capture().Configuration == configuration && _playerAvatarView is not null && _playerAvatar?.Identity == configuration.Avatar &&
                    _player!.ControlProfile == PlayerHostProfiles.Native && Engine.PhysicsTicksPerSecond == configuration.NativeTicksPerSecond,
                    "same-player", "genuine Ratchet, selected native profile and physics cadence across worlds");
                AcceptanceAssert(_hudState.Current.Health is not null && _hudState.Current.Bolts is not null && _playerHud!.Visible, "same-hud", "persistent health/wallet HUD visible");
                await PhysicsFramesAsync(5);
                Require(AssignPlayerQuickSelect(7, "rac3/item/43").Accepted, "capture quick slot unavailable");
                foreach (string item in new[] { "rac2/item/30", "rac3/item/43" })
                {
                    Require(SelectPlayerEquipment(item).Accepted, "capture selection failed: " + item);
                    int before = _fusion!.ShotsFired;
                    Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.B, Pressed = true });
                    await PhysicsFramesAsync(item == "rac3/item/43" ? 4 : 18);
                    Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.B, Pressed = false });
                    await PhysicsFramesAsync(65);
                    AcceptanceAssert(_fusion.ShotsFired > before, "recorded-native-firing", item);
                }
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.F8, Pressed = true });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.F8, Pressed = false });
                await PhysicsFramesAsync(2);
                string bookmarkDirectory = Path.GetFullPath(_args.RecordPlaytest!) + ".bookmarks";
                string bookmark = Path.Combine(bookmarkDirectory, $"{visit++:D2}-" + destination.Replace(':', '-') + ".bookmark.json");
                _playtest.RequestBookmark(bookmark);
                for (int frame = 0; !File.Exists(bookmark) && frame < 600; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                AcceptanceAssert(File.Exists(bookmark) && MovementReproStateFile.Load(bookmark).Destination == destination, "universal-bookmark", "completed prefix and shared profile at " + destination);
                GD.Print("[reconciliation-capture] selection, firing, marker: " + destination);
            }
            WriteAcceptanceEvidence("capture-route");
            _playtest.Save();
        }
        catch (Exception error)
        {
            FinishAcceptanceFailure("capture-route", error, "reconciliation-capture-fail");
        }
    }

    private async Task VerifyFinishedCaptureContinuationAsync()
    {
        AcceptanceAssert(OnDestinationChosen(TrilogyWorldProviders.Resolve("rac2:LEVEL1")!), "finished-capture-entry", "world can change after F9 finishes the tape");
        var player = _player!; long before = _playerPhysicsClock!.Tick;
        await PhysicsFramesAsync(3);
        AcceptanceAssert(_playtest is { Finished: true, PreparingWorld: false } && ReferenceEquals(player, _player) && _playerPhysicsClock.Tick > before,
            "finished-capture-continues", "finished recorder leaves the player and world clocks running");
        WriteAcceptanceEvidence("capture-route");
    }
}
