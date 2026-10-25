using Godot;
using OBP.Godot;

namespace OneBigPackage;

public partial class OBPGame
{
    private TitleScreen? _titleScreen;

    public void ShowTitleFromBootstrap() => ShowTitleScreen();

    private void ClearTitleScreen()
    {
        if (_titleScreen is not null && IsInstanceValid(_titleScreen)) _titleScreen.QueueFree();
        _titleScreen = null;
    }

    private void ShowTitleScreen()
    {
        if (!RequireOrdinarySourceSetup("title")) return;
        _mode = Mode.Title;
        _sceneKind = "title";
        _genericNavigationActive = false;
        TeardownWorld();
        EnsurePlainEnvironment();
        _destinationSelector?.QueueFree(); _destinationSelector = null;
        _sourceManager?.QueueFree(); _sourceManager = null;
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
