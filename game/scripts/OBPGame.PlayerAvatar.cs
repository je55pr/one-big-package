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
    private IPlayerAnimationPresentationSink? _playerAvatarAnimationSink;
    private IPlayerAnimationPresentationController? _playerAvatarAnimationController;
    private double _playerAvatarClock;

    /// <summary>
    /// Replace the debug capsule presentation with the retail-backed R&C1 Ratchet
    /// avatar whenever that source is attached. Controller/collision stay on
    /// DebugPlayer and remain deliberately independent from avatar presentation.
    /// </summary>
    private void AttachRatchetPlayerVisual(DebugPlayer player)
    {
        if (_activeDestination?.Game != ObpSourceGame.Rac1)
        {
            GD.Print("[player-avatar] native player avatar is not recovered for this source game; keeping debug capsule visual");
            return;
        }

        EnsureSourceLibraryInitialized();
        IPlayerAvatarProvider provider = Rac1PlayerAvatarProvider.Instance;
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
                _ratchetPlayerAvatar = provider.Load(
                    source.Path,
                    Rac1PlayerAvatarProvider.RatchetAvatarId);
                _ratchetPlayerAvatarSourcePath = source.Path;
                sw.Stop();
                GD.Print($"[player-avatar] loaded {_ratchetPlayerAvatar.Identity.ModelId} in {sw.ElapsedMilliseconds} ms");
            }

            IPlayerAnimationPresentationController animationController =
                CreatePlayerAnimationController(provider, _ratchetPlayerAvatar);
            animationController.SetAnimationState(player.AnimationState);
            var view = new PlayerAvatarView(
                _ratchetPlayerAvatar,
                animationController.Current,
                alignGeometricBase: true)
            {
                Name = "RatchetAvatar",
            };
            ClearPlayerAvatarView();
            player.VisualRoot.ReplaceVisual(view);
            player.ConfigureAvatarPresentation((float)_ratchetPlayerAvatar.AnimationBounds.Height);
            _playerAvatarView = view;
            _playerAvatarAnimationSink = view;
            _playerAvatarAnimationController = animationController;
            _playerAvatarClock = 0;
            HideAuthoredWorldRatchetPresentation();
            GD.Print($"[player-avatar] attached Ratchet: {view.FrameCount} frames @ {view.FramesPerSecond:0.###} FPS");
        }
        catch (Exception ex)
        {
            _playerAvatarAnimationSink = null;
            _playerAvatarAnimationController = null;
            _playerAvatarView = null;
            _playerAvatarClock = 0;
            player.VisualRoot.ReplaceVisual(null);
            GD.PrintErr($"[player-avatar] could not attach Ratchet; keeping debug capsule: {ex.Message}");
        }
    }

    private static IPlayerAnimationPresentationController CreatePlayerAnimationController(
        IPlayerAvatarProvider provider,
        PlayerAvatar avatar)
    {
        if (provider is IPlayerAnimationControllerProvider sourceAware)
            return sourceAware.CreateAnimationController(avatar);

        GD.Print($"[player-avatar] {provider.SourceGame} has no native animation selector; using generic presentation fallback");
        return new GenericPlayerAnimationPresentationController(avatar);
    }

    private void TickPlayerAvatar(double delta)
    {
        if (_playerAvatarView is null || !GodotObject.IsInstanceValid(_playerAvatarView))
            return;

        _playerAvatarAnimationController?.SetAnimationState(
            _player?.AnimationState ?? PlayerAnimationState.Idle);
        _playerAvatarClock += Math.Max(0, delta);
        if (_playerAvatarAnimationController is not null)
        {
            var presentation = _playerAvatarAnimationController.SetClock(_playerAvatarClock);
            _playerAvatarAnimationSink?.SetAnimationPresentation(presentation);
        }

        _playerAvatarView.SetClock(_playerAvatarClock);
    }

    private void ClearPlayerAvatarView()
    {
        _playerAvatarAnimationSink = null;
        _playerAvatarAnimationController = null;
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
