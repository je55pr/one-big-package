using Godot;
using OBP.Core;
using OBP.Godot.Player;
using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OneBigPackage;

public partial class OBPGame
{
    private PlayerAvatar? _ratchetPlayerAvatar;
    private string? _ratchetPlayerAvatarSourcePath;
    private PlayerAvatarView? _playerAvatarView;
    private double _playerAvatarClock;

    /// <summary>
    /// Replace the debug capsule presentation with the retail-backed R&C1 Ratchet
    /// avatar whenever that source is attached. Controller/collision stay on
    /// DebugPlayer and remain deliberately independent from avatar presentation.
    /// </summary>
    private void AttachRatchetPlayerVisual(DebugPlayer player)
    {
        EnsureSourceLibraryInitialized();
        var source = _sources.Get(ObpSourceGame.Rac1);
        if (source is null)
        {
            GD.Print("[player-avatar] R&C1 source is not attached; keeping debug capsule visual");
            return;
        }

        try
        {
            if (_ratchetPlayerAvatar is null ||
                !string.Equals(_ratchetPlayerAvatarSourcePath, source.Path, StringComparison.OrdinalIgnoreCase))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _ratchetPlayerAvatar = Rac1PlayerAvatarProvider.Instance.Load(
                    source.Path,
                    Rac1PlayerAvatarProvider.RatchetAvatarId);
                _ratchetPlayerAvatarSourcePath = source.Path;
                sw.Stop();
                GD.Print($"[player-avatar] loaded {_ratchetPlayerAvatar.Identity.ModelId} in {sw.ElapsedMilliseconds} ms");
            }

            var view = new PlayerAvatarView(_ratchetPlayerAvatar, alignGeometricBase: true)
            {
                Name = "RatchetAvatar",
            };
            player.VisualRoot.ReplaceVisual(view);
            player.ConfigureAvatarPresentation((float)_ratchetPlayerAvatar.AnimationBounds.Height);
            _playerAvatarView = view;
            _playerAvatarClock = 0;
            HideAuthoredWorldRatchetPresentation();
            GD.Print($"[player-avatar] attached Ratchet: {view.FrameCount} frames @ {view.FramesPerSecond:0.###} FPS");
        }
        catch (Exception ex)
        {
            _playerAvatarView = null;
            _playerAvatarClock = 0;
            player.VisualRoot.ReplaceVisual(null);
            GD.PrintErr($"[player-avatar] could not attach Ratchet; keeping debug capsule: {ex.Message}");
        }
    }

    private void TickPlayerAvatar(double delta)
    {
        if (_playerAvatarView is null || !GodotObject.IsInstanceValid(_playerAvatarView))
        {
            return;
        }

        _playerAvatarClock += Math.Max(0, delta);
        _playerAvatarView.SetClock(_playerAvatarClock);
    }

    private void ClearPlayerAvatarView()
    {
        _playerAvatarView = null;
        _playerAvatarClock = 0;
    }

    /// <summary>
    /// R&C1 still exposes the authored class-0 standing Ratchet through the world
    /// importer for archaeology/equivalence. Once a player avatar is attached,
    /// hide that duplicate Godot presentation only; RuntimeWorld stays untouched.
    /// </summary>
    private void HideAuthoredWorldRatchetPresentation()
    {
        if (_activeDestination?.Game != ObpSourceGame.Rac1 || _sceneResult?.AnimatedMeshes is null)
        {
            return;
        }

        int hidden = 0;
        foreach (var animated in _sceneResult.AnimatedMeshes)
        {
            if (!animated.Name.StartsWith("ratchet_i", StringComparison.Ordinal))
            {
                continue;
            }

            animated.Instance.Visible = false;
            hidden++;
        }

        if (hidden > 0)
        {
            GD.Print($"[player-avatar] hid {hidden} authored Ratchet surface(s) from world presentation");
        }
    }
}
