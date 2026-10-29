using Godot;

namespace OBP.Game;

internal sealed class FusionWeaponPreparationNotice(Node owner) : IDisposable
{
    private CanvasLayer? _layer;
    private Label? _notice;

    internal void Show(string? status, bool hidden)
    {
        if (hidden || status is null)
        {
            if (_layer is not null) _layer.Visible = false;
            return;
        }
        if (_notice is null)
        {
            _layer = new CanvasLayer { Layer = 190 };
            var panel = new PanelContainer
            {
                AnchorLeft = .5f,
                AnchorRight = .5f,
                AnchorTop = 1,
                AnchorBottom = 1,
                OffsetLeft = -360,
                OffsetRight = 360,
                OffsetTop = -66,
                OffsetBottom = -14,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _notice = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            panel.AddChild(_notice);
            _layer.AddChild(panel);
            owner.AddChild(_layer);
        }
        _layer!.Visible = true;
        _notice.Text = "SHARED EQUIPMENT  ·  " + status;
    }

    public void Dispose()
    {
        if (_layer is not null && GodotObject.IsInstanceValid(_layer)) _layer.Free();
        _layer = null;
        _notice = null;
    }
}
