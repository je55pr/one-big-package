using Godot;
using OBP.Godot;

namespace OBP.Game;

public partial class OBPGame
{
    private TitleScreen? _titleScreen;

    // Navigation ownership retires with #347; keep the established bootstrap port.
    public bool TryReturnSelectorToMenu()
    {
        if (_mode != Mode.Selector || _args.CaptureFrame is not null) return false;
        _genericNavigationActive = false;
        _destinationSelector?.QueueFree(); _destinationSelector = null;
        if (Sources.Setup.Access == SourceAccessMode.ExplicitDiagnostic) Sources.Show();
        else ShowTitleScreen();
        return true;
    }


    public void ShowTitleFromBootstrap() => ShowTitleScreen();

    private void ClearTitleScreen()
    {
        if (_titleScreen is not null && IsInstanceValid(_titleScreen)) _titleScreen.QueueFree();
        _titleScreen = null;
    }

    private void ShowTitleScreen()
    {
        if (!Sources.RequireOrdinary("title")) return;
        _mode = Mode.Title;
        _sceneKind = "title";
        _genericNavigationActive = false;
        TeardownWorld();
        EnsurePlainEnvironment();
        _destinationSelector?.QueueFree(); _destinationSelector = null;
        Sources.RetireView();
        if (_worldHud is not null && IsInstanceValid(_worldHud)) _worldHud.Visible = false;
        _camera.Current = true;
        _activeCamera = _camera;
        ApplicationPointer.MouseMode = Input.MouseModeEnum.Visible;
        ClearTitleScreen();
        _titleScreen = new TitleScreen { Notice = PlayerSaveNotice };
        _titleScreen.Dismissed += ShowDestinationSelector;
        AddChild(_titleScreen);
    }
}
