using Godot;
using OBP.Core;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// Live, source-independent Godot failure-path checks for the modal owner.
/// Unlike the Roslyn boundary tests, these execute the coordinator with
/// throwing readers and a real SceneTree, viewport and focus owner.
/// </summary>
public partial class ControllerSmoke
{
    private UiModalCoordinator FaultModalCoordinator(
        Func<PauseMenuUi> pauseBuilder,
        Func<WeaponQuickSelectUi.Entry[]> wheelReader,
        Func<PlayerHost?> player,
        Func<bool>? hasSource = null,
        Action<bool>? showSource = null,
        Action<bool>? showSelector = null)
    {
        var equipment = new ModalEquipmentPorts(
            Catalog: () => [],
            QuickSlots: () => [],
            Equip: _ => new EquipmentChangeResult(false),
            Unequip: () => new EquipmentChangeResult(false),
            SetGadget: (_, _) => new EquipmentChangeResult(false),
            AssignSlot: (_, _) => new EquipmentChangeResult(false),
            AssignNativeSlot: (_, _) => false,
            SelectNativeWeapon: _ => { },
            CurrentWeapon: () => new SourceWeaponKey(ObpSourceGame.Rac1, (int)Rac1WeaponId.FirstRanged),
            SelectWeapon: _ => { },
            CanRepeatWeapon: _ => false,
            WheelEntries: wheelReader);
        return new(new UiModalPorts(
            Host: _game, CanPause: () => true, CanQuickSelect: () => true,
            BuildPauseMenu: pauseBuilder, Player: player,
            HasSourceView: hasSource ?? (() => false),
            SetSourceVisible: showSource ?? (_ => { }),
            SetSelectorVisible: showSelector ?? (_ => { }),
            SetPlayerAudioPaused: _ => { },
            TryMissionInteraction: _ => false,
            TryTaxiInteraction: _ => false,
            Equipment: equipment));
    }

    private async Task CheckFailedSettingsOpenAsync()
    {
        var sources = Find<SourceManagerUi>()!;
        var focus = Focus();
        var mouse = ApplicationPointer.MouseMode;
        bool throwOnce = true;
        var fixture = FaultModalCoordinator(
            () => throw new InvalidOperationException("unused pause builder"),
            () => [], () => null, () => true,
            showSource: visible => sources.Visible = visible,
            showSelector: visible =>
            {
                // Throw after the source page has been hidden and focus lost.
                if (!visible && throwOnce)
                {
                    throwOnce = false;
                    throw new InvalidOperationException("intentional selector attach failure");
                }
            });
        try
        {
            fixture.OpenSettings(fromPause: false);
            await Frames(4);
            Require(!fixture.SettingsOpen && sources.Visible && Focus() == focus &&
                ApplicationPointer.MouseMode == mouse,
                "Failed controller-settings open did not restore source visibility, focus and pointer");

            fixture.OpenSettings(fromPause: false);
            await Frames(4);
            Require(fixture.SettingsOpen, "Controller settings did not recover on the next open");
            fixture.CloseSettings();
            await Frames(4);
            Require(!fixture.SettingsOpen && sources.Visible && Focus() == focus,
                "Controller-settings retry failed to restore the source menu focus");
            GD.Print("[controller-smoke] failed settings attach and retry restore keyboard/gamepad focus PASS");
        }
        finally
        {
            fixture.RetireAll();
        }
    }

    private async Task CheckFailedGameplayModalOpensAsync(PlayerHost player)
    {
        Input.MouseModeEnum previous = ApplicationPointer.MouseMode;
        bool throwWheelOnce = true;
        var fixture = FaultModalCoordinator(
            () => throw new InvalidOperationException("intentional pause presenter failure"),
            () =>
            {
                if (throwWheelOnce)
                {
                    throwWheelOnce = false;
                    throw new InvalidOperationException("intentional wheel catalog failure");
                }
                return [new(new(ObpSourceGame.Rac1, (int)Rac1WeaponId.FirstRanged), "Bomb Glove", "")];
            },
            () => player);
        try
        {
            ApplicationPointer.MouseMode = Input.MouseModeEnum.Captured;
            var captured = ApplicationPointer.MouseMode;
            Require(!fixture.OpenPause() && !GetTree().Paused && !fixture.PauseOpen &&
                ApplicationPointer.MouseMode == captured,
                "Failed pause construction acquired a pause lease or altered pointer mode");

            using var gesture = new InputEventJoypadButton { ButtonIndex = JoyButton.Y, Pressed = true };
            Require(!fixture.OpenQuickSelect(gesture) && !fixture.QuickSelectOpen &&
                !player.QuickSelectOwnsGameplayInput && !GetTree().Paused &&
                ApplicationPointer.MouseMode == captured,
                "Throwing Quick Select equipment reader failed to restore this attempt's captured pointer/input");

            Require(fixture.OpenQuickSelect(gesture), "Quick Select cannot retry after a throwing reader");
            await Frames(3);
            Require(fixture.QuickSelectOpen && player.QuickSelectOwnsGameplayInput && !GetTree().Paused,
                "Quick Select retry failed to take exclusive live input");
            fixture.CloseQuickSelect(retiring: true);
            await Frames(3);
            Require(!fixture.QuickSelectOpen && !player.QuickSelectOwnsGameplayInput &&
                ApplicationPointer.MouseMode == captured && !GetTree().Paused,
                "Quick Select retry did not restore pointer/input/pause state");
            GD.Print("[controller-smoke] throwing pause/wheel readers and successful wheel retry PASS");
        }
        finally
        {
            fixture.RetireAll();
            ApplicationPointer.MouseMode = previous;
        }
    }
}
