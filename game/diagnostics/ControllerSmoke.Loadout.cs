using Godot;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

public partial class ControllerSmoke
{
    private async Task CheckUniversalWorldAsync(PlayerHost player)
    {
        var position = player.GlobalPosition;
        await Frames(20);
        Require(GetTree().Paused && player.GlobalPosition == position, "Loadout navigation advanced the player.");
        var menu = Find<PauseMenuUi>()!;
        Require(menu.FindChild("ObpWeaponGrid", true, false) is GridContainer { Columns: 7 } grid && grid.GetChildCount() == 49,
            "Trilogy weapons are not on one 7x7 board.");
        var args = CommandLineArgs.Parse(OS.GetCmdlineUserArgs());
        string id = args.Rac2Iso is null ? "rac1/item/10" : "rac2/item/30";
        if (args.Rac2Iso is not null)
        {
            ulong deadline = Time.GetTicksMsec() + 60_000;
            while (!_game.ReadPlayerEquipment().Single(entry => entry.Definition.Id == id).Selectable && Time.GetTicksMsec() < deadline)
            { await Task.Delay(10); await Frames(1); }
            menu.ShowUniversalSection("Weapons");
        }
        else
        {
            menu.ShowUniversalSection("Weapons");
        }
        await Capture("weapon-grid");
        ((Button)menu.FindChild("ObpItem_" + id.Replace('/', '_'), true, false)).GrabFocus(); await Frames(3); await Press(JoyButton.A);
        Require(_game.ReadPlayerEquipment().Single(entry => entry.Definition.Id == id).State.Selected, "Pause equip did not select the real controller.");
        for (int tab = 0; tab < 3; tab++) await Press(JoyButton.RightShoulder);
        Require(menu.UniversalSection == "Quick Select", "Controller cannot select Quick Select assignments.");
        await FocusButton("8:", contains: true); await Press(JoyButton.A);
        await FocusButton("CLEAR DIRECTION"); await Press(JoyButton.A);
        Require(_game.ReadPlayerQuickSlots()[7] is null, "Pause clear did not change the real assignment.");
        await FocusButton("ASSIGN TO DIRECTION 8"); await Press(JoyButton.A);
        Require(_game.ReadPlayerQuickSlots()[7].HasValue, "Pause assignment did not reach the real loadout.");
        await Press(JoyButton.RightShoulder);
        Require(menu.UniversalSection == "Settings", "Controller cannot select Settings.");
        await FocusButton("CONTROLLER SETTINGS"); await Press(JoyButton.A); await Press(JoyButton.B);
        Require(GetTree().Paused && Find<PauseMenuUi>() == menu && Focus() is Button, "Settings lost pause state/focus.");
        await Press(JoyButton.RightShoulder);
        await FocusButton("SHOW DEBUG OUTPUT"); await Press(JoyButton.A);
        Require(player.DebugOutputEnabled && GetTree().Paused, "Common developer toggle did not update the player.");
        await Press(JoyButton.Start);
        Require(!GetTree().Paused && Find<PauseMenuUi>() is null, "Start did not resume ordinary gameplay.");
        await Press(JoyButton.Start); menu = Find<PauseMenuUi>()!;
        for (int tab = 0; tab < 5; tab++) await Press(JoyButton.RightShoulder);
        await FocusButton("SHOW DEBUG OUTPUT");
        Require(Focus() is CheckButton { ButtonPressed: true }, "Common developer selection did not persist.");
        await Capture("developer-tab");
        // The ammo tool lives in the same Developer tab and returns to gameplay.
        await FocusButton("REFILL ALL AMMO"); await Press(JoyButton.A);
        Require(!GetTree().Paused && Find<PauseMenuUi>() is null, "Refill all ammo did not return to gameplay.");
        await Press(JoyButton.Start); menu = Find<PauseMenuUi>()!;
        for (int tab = 0; tab < 5; tab++) await Press(JoyButton.RightShoulder);
        await FocusButton("SHOW DEBUG OUTPUT");
        await Press(JoyButton.A); await Press(JoyButton.Start);
        Require(!player.DebugOutputEnabled, "Disabling common diagnostics failed.");
        await Button(JoyButton.Y, true);
        var quick = Find<WeaponQuickSelectUi>();
        Require(quick is not null && quick.Entries.Count >= 15 && !GetTree().Paused, "Common menu broke the real-time weapon wheel.");
        await Axis(JoyAxis.LeftX, 1); await Frames(5);
        Require(player.Rac1AnalogueMagnitude < .001d, "Wheel input reached locomotion.");
        await Axis(JoyAxis.LeftX, 0); await Button(JoyButton.Y, false);
        await Press(JoyButton.Start);
        for (int tab = 0; tab < 4; tab++) await Press(JoyButton.RightShoulder);
        await FocusButton("RETURN TO WORLDS"); await Press(JoyButton.A);
        Require(Find<DestinationSelectorUi>() is not null && !GetTree().Paused, "Common pause could not return to worlds.");
        GD.Print("[controller-smoke] common pause, mixed equipment, eight-direction assignment, settings/focus, debug toggle and retained wheel PASS");
    }
}
