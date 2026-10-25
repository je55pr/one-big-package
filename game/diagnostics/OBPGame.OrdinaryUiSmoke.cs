using System.Text.Json;
using Godot;
using OBP.Core;
using OBP.Godot;

namespace OneBigPackage;

public partial class OBPGame
{
    private async Task RunOrdinaryUiSmokeAsync()
    {
        try
        {
            for (int frame = 0; frame < 8; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(!_diagnosticSourceAccess, "Ordinary navigation must retain the complete trilogy requirement.");
            Require(_sourceManager is null, "Ready startup opened setup.");
            await CheckOrdinaryUiStartupAsync();
            for (int frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_mode == Mode.Selector && _destinationSelector is not null, "Title did not open World Select.");
            PauseLoadoutSmoke.VerifyOrdinarySurface(_destinationSelector!);
            Require(GetViewport().GuiGetFocusOwner() is Button { Disabled: false }, "World Select has no controller focus.");
            Require(TryHandleInteractiveBack() && _mode == Mode.Title, "World Select Back did not return to the title.");
            Require(_sourceManager is null, "World Select Back exposed setup.");
            ShowDestinationSelector();
            var lab = _destinationSelector!.FindChild("FusionLab", true, false) as Button;
            Require(lab is not null, "The playable Lab button is missing.");
            lab!.EmitSignal(Button.SignalName.Pressed);
            for (int frame = 0; frame < 4; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_world is not null && _mode == Mode.World, "The playable Lab did not load.");
            PauseLoadoutSmoke.VerifyOrdinarySurface(_ui);
            PauseLoadoutSmoke.VerifyOrdinarySurface(_sceneResult!.Root);
            Require(TryOpenPauseMenu(), "The ordinary pause menu did not open.");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            foreach (string section in new[] { "Weapons", "Gadgets", "Appearance", "Quick Select", "Settings" })
            {
                _pauseMenu!.ShowUniversalSection(section);
                PauseLoadoutSmoke.VerifyOrdinarySurface(_pauseMenu);
            }
            Require(TryClosePauseMenu(), "The pause menu did not close.");
            ShowDestinationSelector();
            var before = _playerSession!.Capture();
            byte[]? saved = SharedProfilePath is { } path && File.Exists(path) ? File.ReadAllBytes(path) : null;
            Require(_sources.Remove(ObpSourceGame.Rac3), "The repair test requires three configured games.");
            ShowTitleScreen();
            Require(_sourceManager is not null && _mode == Mode.Picker && _titleScreen is null, "Genuine source loss did not replace the title with repair.");
            var after = _playerSession.Capture();
            Require(before.Configuration == after.Configuration && JsonSerializer.Serialize(before.Loadout) == JsonSerializer.Serialize(after.Loadout) && before.Health == after.Health,
                "Repair reset the persistent player session.");
            Require(!SaveSharedProfile("ordinary UI repair check"), "Repair allowed a profile write.");
            if (saved is not null) Require(saved.AsSpan().SequenceEqual(File.ReadAllBytes(SharedProfilePath!)), "Repair rewrote the saved game.");
            GD.Print("[ordinary-ui-smoke] PASS: title, resume, World Select/back, Lab, pause/focus and repair preserve ordinary UI and player state");
        }
        catch (Exception error)
        {
            GD.PrintErr("[ordinary-ui-smoke] FAIL: " + error);
            ApplicationLifecycle.RequestQuit(this, "ordinary-ui-smoke-fail", 1);
        }
    }

    private async Task CheckOrdinaryUiStartupAsync()
    {
        Require(_args.OrdinaryUiCase is "fresh" or "resume" or "invalid-save", "Choose an explicit ordinary UI case.");
        if (_args.OrdinaryUiCase == "resume")
        {
            Require(_world is not null && _profileDocument?.Destination == _currentDestinationId, "The resume case did not auto-resume the saved destination.");
            ShowDestinationSelector();
            return;
        }
        Require(_world is null && _mode == Mode.Title && _titleScreen is not null, "Fresh/recovery startup did not show the title.");
        if (_args.OrdinaryUiCase == "invalid-save")
        {
            Require(_profileWriteBlocked, "The invalid-save case did not block writes.");
            Require(_titleScreen!.FindChild("SaveNotice", true, false) is Label { Visible: true, Text: var notice } &&
                notice.Contains("could not be opened", StringComparison.Ordinal) && notice.Contains("without saving", StringComparison.Ordinal), "Invalid-save recovery did not show the read-only notice.");
            Require(!SaveSharedProfile("ordinary UI invalid-save check"), "Invalid-save recovery allowed a write.");
        }
        else Require(!_profileWriteBlocked, "Fresh startup unexpectedly blocked saving.");
        PauseLoadoutSmoke.VerifyOrdinarySurface(_titleScreen!);
        _titleScreen!._UnhandledInput(new InputEventJoypadButton { ButtonIndex = JoyButton.Start, Pressed = true });
        for (int frame = 0; frame < 120 && _mode != Mode.Selector; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
