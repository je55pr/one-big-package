using Godot;
using OBP.Godot;

namespace OBP.Game;

/// <summary>Six-second development notice, independent of world and player lifetimes.</summary>
internal sealed class GodotMovementStateNotice(
    Node owner, HostOperationScope operations, Func<CanvasLayer> getUi,
    Func<PauseMenuUi?> getPauseMenu, Func<bool> debugEnabled)
{
    private Label? _notice;
    private int _noticeGeneration;

    public void Hide()
    {
        if (_notice is not null && GodotObject.IsInstanceValid(_notice)) _notice.Visible = false;
        ++_noticeGeneration;
    }

    public void Report(string message)
    {
        GD.Print($"[movement-state] {message}");
        if (operations.Stopping) return;
        getPauseMenu()?.SetMovementStatus(message);
        if (!debugEnabled()) return;
        if (_notice is null || !GodotObject.IsInstanceValid(_notice) || _notice.IsQueuedForDeletion())
        {
            _notice = new Label
            {
                Name = "DevelopmentNotice",
                AnchorTop = 1f,
                AnchorBottom = 1f,
                AnchorRight = 1f,
                OffsetLeft = 16f,
                OffsetRight = -16f,
                OffsetTop = -64f,
                OffsetBottom = -12f,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _notice.AddThemeColorOverride("font_color", new Color(1f, .95f, .5f));
            getUi().AddChild(_notice);
        }
        _notice.Text = message;
        _notice.Visible = true;
        if (!operations.Stopping)
            operations.Start("movement-state-notice", token => ClearMovementStateNoticeAsync(++_noticeGeneration, token), lifetime: CancellationToken.None);
    }

    private async Task ClearMovementStateNoticeAsync(int generation, CancellationToken cancellationToken)
    {
        await GodotAsyncWait.SignalAsync(owner.GetTree().CreateTimer(6d, processAlways: true), SceneTreeTimer.SignalName.Timeout, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (generation == _noticeGeneration && GodotObject.IsInstanceValid(_notice))
            _notice!.Visible = false;
    }
}
