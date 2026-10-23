using Godot;

namespace OneBigPackage;

/// <summary>
/// R&C1 item grid cell. FUN_0021d948 (draw_menu_item_grid) draws an icon
/// without a text button; a selected cell receives two nested rectangles.
/// The bounding boxes are host-scaled until retail grid geometry is recovered.
/// </summary>
public sealed partial class Rac1RetailEquipmentCell : Button
{
    public ushort? NativeAnimationId { get; init; }
    public bool Owned { get; init; }
    public bool Equipped { get; set; }
    public bool Upgraded { get; init; }
    public bool SuppressEquippedFrame { get; init; }

    private ulong _elapsedFrames;

    public Rac1RetailEquipmentCell()
    {
        MouseDefaultCursorShape = CursorShape.PointingHand;
        FocusMode = FocusModeEnum.All;
        CustomMinimumSize = new Vector2(112, 94);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ClipContents = true;
        var empty = new StyleBoxEmpty();
        foreach (string state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
            AddThemeStyleboxOverride(state, empty);
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (!IsInsideTree()) return;
        MouseFilter = UiPointerModality.ControllerWasLastInput ?
            MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        _elapsedFrames += (ulong)Math.Max(0, Math.Round(delta * 60));
        if (HasFocus() || (!UiPointerModality.ControllerWasLastInput && IsHovered()))
            QueueRedraw();
    }

    public override void _Draw()
    {
        bool selected = HasFocus() ||
            (!UiPointerModality.ControllerWasLastInput && IsHovered());
        if (selected)
        {
            // Native selected cell (FUN_0021d948): pulsating pale border
            // behind a solid inner rect. Approximate scaling only.
            int phase = (int)(_elapsedFrames & 0x3F);
            float level = (.45f + .35f * (phase <= 32 ? phase / 32f : (64 - phase) / 32f));
            var outer = new Rect2(3, 3, Size.X - 6, Size.Y - 6);
            DrawRect(outer, new Color(level, level, level, .80f));
            DrawRect(outer.Grow(-4), new Color(.02f, .025f, .04f, 1f));
        }

        if (!Owned || NativeAnimationId is not ushort id) return;
        int offset = Equipped && !SuppressEquippedFrame ? 1 : Upgraded ? 4 : 0;
        var icon = Rac1RetailUiAssets.Sprite(id, offset) ??
                   Rac1RetailUiAssets.Sprite(id, 0);
        if (icon is null) return;
        float size = Math.Min(78f, Math.Min(Size.X - 20, Size.Y - 12));
        Vector2 topLeft = (Size - new Vector2(size, size)) / 2;
        DrawTextureRect(icon, new Rect2(topLeft, new Vector2(size, size)), false);
    }
}
