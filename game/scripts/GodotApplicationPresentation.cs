using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.Runtime;
using OBP.Runtime.Player;
using OBP.Runtime.Presentation;

namespace OBP.Game;

/// <summary>Application-owned scene roots, player node, cameras and HUD/inspection presentation.</summary>
internal sealed class GodotApplicationPresentation(Node3D host, Func<bool> debug)
{
    private readonly Node3D _host = host;
    private readonly Func<bool> _debug = debug;
    private Node3D _worldRoot = null!;
    private Node3D _playerRoot = null!;
    private Node3D _cameraRoot = null!;
    private CanvasLayer _ui = null!;
    private Camera3D _camera = null!;
    private Camera3D _activeCamera = null!;
    private DebugOverlay? _overlay;
    private WorldInspectorPanel? _inspector;
    private PlayerHost? _player;
    private Label? _worldHud;
    private PlayerHud? _playerHud;

    internal Node3D WorldRoot => _worldRoot;
    internal Node3D PlayerRoot => _playerRoot;
    internal CanvasLayer Ui => _ui;
    internal Camera3D Camera => _camera;
    internal Camera3D ActiveCamera { get => _activeCamera; set => _activeCamera = value; }
    internal DebugOverlay? Overlay => _overlay;
    internal WorldInspectorPanel? Inspector => _inspector;
    internal PlayerHost? Player => _player;
    internal Label? WorldHud => _worldHud;
    internal PlayerHud? PlayerHud => _playerHud;

    internal void BuildSkeleton()
    {
        _worldRoot = new Node3D { Name = "WorldRoot" };
        _playerRoot = new Node3D { Name = "PlayerRoot" };
        _cameraRoot = new Node3D { Name = "CameraRoot" };
        _ui = new CanvasLayer { Name = "UI" };
        _host.AddChild(_worldRoot);
        _host.AddChild(_playerRoot);
        _host.AddChild(_cameraRoot);
        _host.AddChild(_ui);

        _camera = new Camera3D { Name = "Camera3D", Current = true, Far = 12000f };
        _cameraRoot.AddChild(_camera);
        _activeCamera = _camera;
    }

    internal void EnsurePlayerHud()
    {
        if (_playerHud is not null && GodotObject.IsInstanceValid(_playerHud))
        {
            return;
        }

        _playerHud = new PlayerHud { DebugOutputEnabled = _debug() };
        _ui.AddChild(_playerHud);
    }

    internal void UpdatePlayerHud(HudSnapshot hud)
    {
        if (_playerHud is not null && GodotObject.IsInstanceValid(_playerHud))
        {
            _playerHud.Render(hud);
        }
    }

    internal void EnsureWorldHud()
    {
        if (_worldHud is not null && GodotObject.IsInstanceValid(_worldHud))
        {
            _worldHud.Visible = _debug();
            return;
        }

        _worldHud = new WorldHud { Visible = _debug() };
        _ui.AddChild(_worldHud);
    }

    internal void UpdateWorldHud(WorldSceneLease<GodotWorldResources>? entry, WorldHost worldHost, int switches, bool worldActive)
    {
        if (_worldHud is not WorldHud hud) return;
        bool visible = _debug() && worldActive && entry?.Plan.Prepared.World is not null;
        if (!visible)
        {
            hud.Render(null, false);
            return;
        }
        if (entry?.Plan.Prepared.World is not { } world || entry?.Resources.Scene is not { } scene)
        {
            hud.Render(null, false);
            return;
        }

        var player = _player is { } candidate && GodotObject.IsInstanceValid(candidate) ? candidate : null;
        var resources = entry?.Resources;
        hud.Render(new WorldHudFrame(
            world, scene, entry?.NativeDestination, switches,
            player?.GlobalPosition, player?.IsOnFloor() == true,
            _overlay?.StatusLine() ?? "overlays: off",
            worldHost.AudioDiagnostics.Count > 0, worldHost.AudioStatusLine,
            resources?.CrateDiagnostics.HudLine(resources.Rac2CrateBolts) ?? string.Empty,
            resources?.Rac2Gameplay?.HudLine() ?? string.Empty,
            resources?.Rac1Gameplay?.Hud.Line() ?? string.Empty,
            resources?.Rac1Gameplay?.Taxis.HudLine() ?? string.Empty,
            resources?.Rac3Gameplay?.HudLine() ?? string.Empty), true);
    }


    internal void SetupOverlay(RuntimeWorldScene.Result result, RuntimeWorld world, WorldHost worldHost,
        string? spec, bool inspect)
    {
        _overlay = new DebugOverlay(result, world);
        ApplyOverlaySpec(spec);
        _inspector = new WorldInspectorPanel(result, world, worldHost);
        _ui.AddChild(_inspector);
        if (inspect && _debug()) Callable.From(OpenInspectorDeferred).CallDeferred();
    }

    private void OpenInspectorDeferred()
    {
        if (_debug()) _inspector?.Toggle();
    }

    internal void ApplyOverlaySpec(string? spec) => _overlay?.ApplySpec(spec, message => GD.PrintErr(message));

    internal PlayerHost SpawnPlayer(RuntimeWorld world, RuntimeSpawn? start, float snapReach,
        CommandLineArgs args, PlayerControlProfile? profile, Func<GodotSelectedPlayerSpawnPose?> crateFocus)
    {
        bool scripted = args.CaptureFrame is not null;
        var placement = GodotPlayerSpawnPlacement.Resolve(world, start, args.AnimFocus, scripted, crateFocus,
            message => GD.Print(message));
        var player = new PlayerHost
        {
            Name = "PlayerHost",
            Scripted = scripted,
            ScriptedStill = args.CrateFocus && !args.CrateAutoStrike,
            ControlProfile = profile ?? PlayerHostProfiles.Diagnostic,
            // Placement clearance was staged with the world. OBP deliberately
            // reuses the retail-derived R&C1 controller in every trilogy world;
            // seed its facing from the authored pose regardless of world origin.
            InitialGroundSnapUpwardReach = snapReach,
            Rac1CurrentYaw = -placement.Yaw,
            Position = placement.Position,
        };
        _playerRoot.AddChild(player);
        player.SetDebugOutputEnabled(_debug());
        player.RotateY(placement.Yaw);
        _camera.Current = false;
        _activeCamera = player.Camera;
        _player = player;
        return player;
    }

    internal void RetirePlayerAndInspection()
    {
        if (_player?.GetParent() is { } parent) parent.RemoveChild(_player);
        _player?.QueueFree();
        _player = null;
        _overlay = null;
        _inspector?.QueueFree();
        _inspector = null;
    }

    internal void RetireWorldHud(HudStateAdapter state, bool ordinary)
    {
        if (!ordinary) state.ResetSession();
        _playerHud?.Render(state.Current);
    }

    internal void SetDebugOutputEnabled(bool enabled, Action hideMovementNotice)
    {
        _player?.SetDebugOutputEnabled(enabled);
        if (_playerHud is not null && GodotObject.IsInstanceValid(_playerHud)) _playerHud.DebugOutputEnabled = enabled;
        if (!enabled)
        {
            if (_inspector is { IsOpen: true }) _inspector.Toggle();
            _overlay?.Clear();
            hideMovementNotice();
        }
    }
}
