using Godot;
using OBP.Godot;

namespace OBP.Game;

/// <summary>The replay driver's scene boundary; no root or player node escapes this adapter.</summary>
internal sealed class GodotPlaytestHost(Node host, Func<PlayerHost?> player, Func<PauseMenuUi?> pauseMenu)
{
    private PlayerHost? _attachedPlayer;
    private ReplayInputLease<RawPlayerInputFrame>? _input;

    internal Vector2I WindowSize { get => host.GetWindow().Size; set => host.GetWindow().Size = value; }
    internal bool WindowFocused => host.GetWindow().HasFocus();
    internal void Quit(string reason, int code = 0) => ApplicationLifecycle.RequestQuit(host, reason, code);

    internal void SetSimulationEnabled(bool enabled)
    {
        host.SetPhysicsProcess(enabled);
        player()?.SetPhysicsProcess(enabled);
    }

    internal void AttachPlayerInput(Func<RawPlayerInputFrame, RawPlayerInputFrame> filter)
    {
        var next = player();
        if (ReferenceEquals(next, _attachedPlayer)) return;
        ReleasePlayerInput();
        if (next is null || !GodotObject.IsInstanceValid(next) || !next.IsInsideTree()) return;
        _attachedPlayer = next;
        _input = new(() => ReferenceEquals(player(), next) && GodotObject.IsInstanceValid(next) && next.IsInsideTree() && !next.IsQueuedForDeletion(),
            filter, callback =>
            {
                if (!GodotObject.IsInstanceValid(next)) return;
                if (next.PlaytestInputFilter == callback) next.PlaytestInputFilter = null;
                next.TreeExiting -= ReleasePlayerInput;
            });
        next.PlaytestInputFilter = _input.Filter;
        next.TreeExiting += ReleasePlayerInput;
    }

    internal void ReleasePlayerInput()
    {
        var input = _input;
        _input = null;
        _attachedPlayer = null;
        input?.Dispose();
    }

    internal IDisposable SuspendUi() => new UiSuspension(host, pauseMenu());

    private sealed class UiSuspension : IDisposable
    {
        private readonly SceneTree _tree;
        private readonly Node _root;
        private readonly bool _paused, _pauseVisible, _navigationSuspended;
        private readonly Input.MouseModeEnum _mouse;
        private readonly Control? _focus;
        private readonly PauseMenuUi? _pause;
        private readonly List<(Node Node, Node.ProcessModeEnum Mode)> _nodes = [];
        private bool _disposed;

        internal UiSuspension(Node host, PauseMenuUi? pause)
        {
            _tree = host.GetTree();
            _root = _tree.Root;
            _paused = _tree.Paused;
            _mouse = ApplicationPointer.MouseMode;
            _focus = host.GetViewport().GuiGetFocusOwner();
            _navigationSuspended = ControllerBindings.MenuNavigationSuspended;
            _pause = pause;
            _pauseVisible = pause?.Visible == true;
            // Ordinary nodes obey tree pause; explicitly processing menus must
            // also stop while the report form consumes text and controller input.
            foreach (var node in host.FindChildren("*", "", true, false))
                if (node.ProcessMode is not (Node.ProcessModeEnum.Inherit or Node.ProcessModeEnum.Disabled))
                {
                    _nodes.Add((node, node.ProcessMode));
                    node.ProcessMode = Node.ProcessModeEnum.Disabled;
                }
            ControllerBindings.MenuNavigationSuspended = false;
            if (pause is not null) pause.Visible = false;
            _tree.Paused = true;
            ApplicationPointer.MouseMode = Input.MouseModeEnum.Visible;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var action in InputMap.GetActions()) Input.ActionRelease(action);
            if (GodotObject.IsInstanceValid(_root))
                foreach (var navigation in _root.FindChildren("*", "", true, false).OfType<ControllerMenuNavigation>())
                    navigation.ResetRepeat();
            foreach (var (node, mode) in _nodes)
                if (GodotObject.IsInstanceValid(node)) node.ProcessMode = mode;
            _nodes.Clear();
            ControllerBindings.MenuNavigationSuspended = _navigationSuspended;
            if (_pause is not null && GodotObject.IsInstanceValid(_pause)) _pause.Visible = _pauseVisible;
            if (MenuFocus.Available(_focus)) _focus!.GrabFocus();
            if (GodotObject.IsInstanceValid(_tree)) _tree.Paused = _paused;
            ApplicationPointer.MouseMode = _mouse;
        }
    }
}
