using Godot;
using OBP.Godot;
using OBP.RAC1.Progression;

namespace OBP.Game;

/// <summary>
/// One modal planet-map page. Owns its input, focus, player suppression and
/// tree-pause lifetime; the campaign session alone determines travel admission.
/// </summary>
internal sealed class Rac1PlanetMapPage(Node3D host, Func<PlayerHost?> player)
{
    private readonly Node3D _host = host;
    private readonly Func<PlayerHost?> _player = player;
    private Rac1PlanetTravelUi? _view;
    private PlayerHost? _pausedPlayer;
    private Node.ProcessModeEnum? _previousPlayerMode;
    private Input.MouseModeEnum _previousMouseMode;
    private int _heldButtons;
    private bool _ownsPause;
    private int _pageGeneration;

    internal bool IsOpen => _view is not null && GodotObject.IsInstanceValid(_view);

    internal bool Open(Rac1PlanetMapSnapshot snapshot, Func<int, Rac1PlanetTravelStartResult> travel)
    {
        if (IsOpen) return true;
        _pausedPlayer = _player() is { } player && GodotObject.IsInstanceValid(player)
            ? player : null;
        if (_pausedPlayer is not null)
        {
            _previousPlayerMode = _pausedPlayer.ProcessMode;
            _pausedPlayer.ProcessMode = Node.ProcessModeEnum.Disabled;
        }
        _previousMouseMode = ApplicationPointer.MouseMode;
        _heldButtons = _player()?.MenuHeldButtons ?? 0;
        ApplicationPointer.MouseMode = Input.MouseModeEnum.Visible;

        var view = new Rac1PlanetTravelUi { ProcessMode = Node.ProcessModeEnum.Always };
        _view = view;
        int generation = ++_pageGeneration;
        view.TravelRequested += id =>
        {
            if (generation != _pageGeneration || !ReferenceEquals(_view, view)) return;
            var result = travel(id);
            if (generation != _pageGeneration || !ReferenceEquals(_view, view)) return;
            switch (result)
            {
                case Rac1PlanetTravelStartResult.Started:
                    Close();
                    break;
                case Rac1PlanetTravelStartResult.AlreadyCurrentLevel:
                    view.SetHint("That is the current world.", error: true);
                    break;
                case Rac1PlanetTravelStartResult.DestinationUnavailable:
                    view.SetHint("That destination is no longer available.", error: true);
                    break;
            }
        };
        view.BackRequested += () =>
        {
            if (generation == _pageGeneration) Close();
        };
        _host.AddChild(view);
        view.Populate(snapshot);
        _host.GetTree().Paused = true;
        _ownsPause = true;
        GD.Print($"[rac1-travel] planet map shown — {snapshot.UnlockedDestinations.Count} recovered destination(s)");
        return true;
    }

    internal bool Close()
    {
        if (_view is null) return false;
        ++_pageGeneration;
        if (GodotObject.IsInstanceValid(_view))
        {
            _view.ProcessMode = Node.ProcessModeEnum.Disabled;
            _view.QueueFree();
        }
        _view = null;
        if (_ownsPause)
        {
            _host.GetTree().Paused = false;
            _ownsPause = false;
        }
        if (_pausedPlayer is { } player && GodotObject.IsInstanceValid(player) &&
            _previousPlayerMode is { } oldMode)
            player.ProcessMode = oldMode;

        _pausedPlayer = null;
        _previousPlayerMode = null;
        ApplicationPointer.MouseMode = _previousMouseMode;
        _player()?.SuppressButtonsUntilRelease(_heldButtons);
        return true;
    }
}
