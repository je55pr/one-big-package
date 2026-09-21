using Godot;
using OBP.Core;
using OBP.Godot.Player;
using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OneBigPackage;

public partial class OBPGame
{
    private static readonly PlayerAvatarProviderRegistry PlayerAvatarProviders = new(
        [Rac1PlayerAvatarProvider.Instance]);

    private PlayerAvatar? _playerAvatar;
    private string? _playerAvatarSourcePath;
    private ObpSourceGame? _playerAvatarSourceGame;
    private PlayerAvatarView? _playerAvatarView;
    private IPlayerAnimationPresentationSink? _playerAvatarAnimationSink;
    private IPlayerAnimationPresentationController? _playerAvatarAnimationController;
    private double _playerAvatarClock;

    /// <summary>
    /// Resolve player presentation from the active source game. A missing native
    /// avatar provider is an explicit unsupported boundary: keep the debug capsule
    /// rather than borrowing another game's model, clips, or animation selector.
    /// Controller/collision stay on DebugPlayer and remain independent from visuals.
    /// </summary>
    private void AttachPlayerAvatarVisual(DebugPlayer player)
    {
        if (_activeDestination is not { } destination)
            return;

        IPlayerAvatarProvider? provider = PlayerAvatarProviders.Get(destination.Game);
        if (provider is null)
        {
            GD.Print($"[player-avatar] {destination.Game} has no decoded native player-avatar provider; keeping debug capsule visual");
            return;
        }

        EnsureSourceLibraryInitialized();
        var source = _sources.Get(destination.Game);
        if (source is null)
        {
            GD.Print($"[player-avatar] {destination.Game} source is not attached; keeping debug capsule visual");
            return;
        }

        try
        {
            if (_playerAvatar is null ||
                _playerAvatarSourceGame != destination.Game ||
                !string.Equals(_playerAvatarSourcePath, source.Path, StringComparison.OrdinalIgnoreCase))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _playerAvatar = provider.Load(source.Path, provider.DefaultAvatarId);
                _playerAvatarSourcePath = source.Path;
                _playerAvatarSourceGame = destination.Game;
                sw.Stop();
                GD.Print($"[player-avatar] loaded {_playerAvatar.Identity.ModelId} in {sw.ElapsedMilliseconds} ms");
            }

            IPlayerAnimationPresentationController animationController =
                CreatePlayerAnimationController(provider, _playerAvatar);
            animationController.SetAnimationState(player.AnimationState);
            var view = new PlayerAvatarView(
                _playerAvatar,
                animationController.Current,
                alignGeometricBase: true)
            {
                Name = "PlayerAvatar",
            };
            ClearPlayerAvatarView();
            player.VisualRoot.ReplaceVisual(view);
            player.ConfigureAvatarPresentation((float)_playerAvatar.AnimationBounds.Height);
            _playerAvatarView = view;
            _playerAvatarAnimationSink = view;
            _playerAvatarAnimationController = animationController;
            _playerAvatarClock = 0;
            HideAuthoredWorldRatchetPresentation();
            GD.Print($"[player-avatar] attached {_playerAvatar.Identity.ModelId}: {view.FrameCount} frames @ {view.FramesPerSecond:0.###} FPS");
        }
        catch (Exception ex)
        {
            _playerAvatarAnimationSink = null;
            _playerAvatarAnimationController = null;
            _playerAvatarView = null;
            _playerAvatarClock = 0;
            player.VisualRoot.ReplaceVisual(null);
            GD.PrintErr($"[player-avatar] could not attach native avatar; keeping debug capsule: {ex.Message}");
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

    private int? CurrentPlayerAvatarSourceSequence()
    {
        string? key = _playerAvatarAnimationSink?.CurrentAnimationPresentation.SourceSequenceKey;
        return int.TryParse(
            key,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out int sequenceId)
            ? sequenceId
            : null;
    }

    private bool PlayerAvatarPresentationIsSynchronized() =>
        _playerAvatarAnimationSink?.CurrentAnimationPresentation ==
        _playerAvatarAnimationController?.Current;

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
