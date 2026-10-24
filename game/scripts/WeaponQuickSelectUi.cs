using Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Presentation;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

/// <summary>
/// Eight-direction R&C1 quick-selection presentation. Source-native ring
/// placement and eight-slot inventory; host-only overflow paging is retained
/// for partially recovered equipment sets. The player/world is not paused.
/// </summary>
public sealed partial class WeaponQuickSelectUi : CanvasLayer
{
    public sealed record Entry(SourceWeaponKey Weapon, string Name, string Ammo);
    public IReadOnlyList<Entry> Entries { get; init; } = [];
    public SourceWeaponKey CurrentWeapon { get; init; }
    public event Action<SourceWeaponKey?>? Completed;
    public SourceWeaponKey? SelectedWeapon =>
        SelectedIndex is { } index && index < Entries.Count ? Entries[index].Weapon : null;
    public int? SelectedIndex { get; private set; }
    public bool NeutralTapCompleted { get; private set; }

    private sealed partial class WheelFace : Control
    {
        public WeaponQuickSelectUi UiOwner = null!;
        public override void _Draw() => UiOwner.DrawWheel(this);
    }

    private WheelFace _wheel = null!;
    private ulong _openedAt;
    private bool _directed;
    private int _page;

    public override void _Ready()
    {
        Name = "WeaponQuickSelect";
        Layer = 240;
        ProcessMode = ProcessModeEnum.Always;
        _openedAt = PlaytestClock.Milliseconds;

        _wheel = new WheelFace
        {
            UiOwner = this,
            AnchorRight = 1,
            AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_wheel);

        int current = Entries.ToList().FindIndex(entry => entry.Weapon == CurrentWeapon);
        if (current >= 0)
        {
            _page = current / 8;
            SelectedIndex = current;
        }
    }

    public override void _Process(double delta)
    {
        float x = Input.GetActionStrength(RawGamepadInput.MoveRight) -
                  Input.GetActionStrength(RawGamepadInput.MoveLeft);
        float y = Input.GetActionStrength(RawGamepadInput.MoveBack) -
                  Input.GetActionStrength(RawGamepadInput.MoveForward);

        if (x * x + y * y >= 0.25f)
        {
            _directed = true;
            int slot = ((int)MathF.Floor((MathF.Atan2(x, -y) + MathF.PI / 8f) /
                       (MathF.PI / 4f)) + 8) % 8;
            int index = _page * 8 + slot;
            if (index < Entries.Count)
                SelectedIndex = index;
        }
        _wheel.QueueRedraw();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionReleased(ControllerBindings.QuickSelect))
        {
            NeutralTapCompleted = !_directed && PlaytestClock.Milliseconds - _openedAt < 250;
            Completed?.Invoke(SelectedWeapon);
        }
        else if (@event.IsActionPressed("ui_cancel") &&
                 !@event.IsActionPressed(ControllerBindings.QuickSelect))
            Completed?.Invoke(null);
        else if (@event.IsActionPressed("ui_accept"))
            Completed?.Invoke(SelectedWeapon);
        else if (@event.IsActionPressed("ui_focus_next") ||
                 @event.IsActionPressed("ui_focus_prev"))
        {
            int pages = Math.Max(1, (Entries.Count + 7) / 8);
            if (pages > 1)
            {
                _directed = true;
                _page = (_page + (@event.IsActionPressed("ui_focus_next") ? 1 : pages - 1)) % pages;
                SelectedIndex = null;
            }
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            Vector2 delta = click.Position - _wheel.Size / 2f;
            float radius = Radius(_wheel.Size);
            if (delta.Length() < radius * 0.60f || delta.Length() > radius * 1.42f)
                return;
            int slot = ((int)MathF.Floor((MathF.Atan2(delta.X, -delta.Y) + MathF.PI / 8f) /
                       (MathF.PI / 4f)) + 8) % 8;
            int index = _page * 8 + slot;
            if (index >= Entries.Count)
                return;
            Completed?.Invoke(Entries[index].Weapon);
        }
        else
            return;
        GetViewport().SetInputAsHandled();
    }

    private static float Radius(Vector2 size) => Mathf.Max(112f, Mathf.Min(size.X, size.Y) * 0.5f - 90f);

    private void DrawWheel(WheelFace face)
    {
        Vector2 size = face.Size;
        Vector2 center = size * 0.5f;
        float radius = Radius(size);
        float unit = Mathf.Clamp(Mathf.Min(size.X, size.Y) / 520f, 0.85f, 2.2f);
        float tile = 54f * unit;

        face.DrawRect(new Rect2(Vector2.Zero, size), new Color(0f, 0f, 0f, 0.17f));
        face.DrawCircle(center, radius + 0.54f * tile, new Color(0.015f, 0.025f, 0.040f, 0.43f));
        face.DrawArc(center, radius, 0f, Mathf.Tau, 96, new Color(0.75f, 0.60f, 0.29f, 0.57f), 2f * unit, true);

        Font font = Rac1RetailUiAssets.Font("regular") ?? ThemeDB.FallbackFont;
        Font small = Rac1RetailUiAssets.Font("small") ?? font;
        int fontSize = Mathf.RoundToInt(15f * unit);
        int smallSize = Mathf.RoundToInt(12f * unit);
        Color cream = new(1f, 0.86f, 0.63f);
        int current = Entries.ToList().FindIndex(entry => entry.Weapon == CurrentWeapon);

        for (int slot = 0; slot < 8; slot++)
        {
            float theta = slot * Mathf.Pi / 4f - Mathf.Pi / 2f;
            Vector2 point = center + new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)) * radius;
            int index = _page * 8 + slot;
            bool valid = index < Entries.Count;
            bool selected = SelectedIndex == index;
            float pulse = (float)(0.5f + 0.5f * Math.Sin(PlaytestClock.Milliseconds / 170.0));
            var rect = new Rect2(point - Vector2.One * tile * 0.5f, Vector2.One * tile);
            face.DrawRect(rect, selected
                ? new Color(0.18f, 0.22f, 0.32f, 0.95f)
                : new Color(0.04f, 0.055f, 0.085f, 0.73f));
            face.DrawRect(rect, selected ? new Color(1f, 0.85f, 0.32f, 0.75f + 0.25f * pulse)
                : new Color(0.32f, 0.37f, 0.45f, 0.8f), false, selected ? 4f * unit : 1.5f * unit);
            if (!valid)
                continue;

            var entry = Entries[index];
            var mappedIcon = entry.Weapon.SourceGame == "rac1"
                ? Rac1WeaponIconCatalog.Ordinary((Rac1WeaponId)entry.Weapon.NativeItemId) : null;
            var icon = mappedIcon is { } mapped
                ? Rac1RetailUiAssets.Sprite(mapped.AnimationId, mapped.Frame)
                : null;
            if (icon is not null)
            {
                float iconSize = tile * 0.88f;
                face.DrawTextureRect(icon, new Rect2(point - Vector2.One * (iconSize * 0.5f),
                    Vector2.One * iconSize), false);
            }
            else
            {
                // Unmapped equipment and unavailable sources retain a readable fallback.
                string letters = string.Join("", entry.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(word => char.ToUpperInvariant(word[0]))).PadRight(2).Substring(0, 2);
                face.DrawString(font, new Vector2(rect.Position.X + 2f * unit, point.Y + 5f * unit),
                    letters, HorizontalAlignment.Center, tile - 4f * unit, fontSize + 5, cream);
            }
            if (current == index)
                face.DrawCircle(point + new Vector2(tile * 0.32f, -tile * 0.32f),
                    3.5f * unit, UiTheme.Ready);

            float labelWidth = 150f * unit;
            float labelY = point.Y + tile * 0.70f;
            face.DrawString(small, new Vector2(point.X - labelWidth / 2f, labelY),
                entry.Name, HorizontalAlignment.Center, labelWidth, smallSize, Colors.White);
            if (selected && !string.IsNullOrWhiteSpace(entry.Ammo))
                face.DrawString(small, new Vector2(point.X - labelWidth / 2f, labelY + 15f * unit),
                    entry.Ammo, HorizontalAlignment.Center, labelWidth, smallSize, cream);
        }

        var triangle = Rac1RetailUiAssets.Sprite(0x7544, 0);
        if (triangle is not null)
        {
            float icon = 20f * unit;
            face.DrawTextureRect(triangle,
                new Rect2(center.X - icon * 0.5f, center.Y - 48f * unit,
                          icon, icon), false);
        }
        float labelW = 180f * unit;
        face.DrawString(font, new Vector2(center.X - labelW / 2, center.Y + 3f * unit),
            Rac1RetailUiAssets.Text(20196, "QUICK SELECT"), HorizontalAlignment.Center, labelW, fontSize, cream);
        face.DrawString(small, new Vector2(center.X - labelW / 2, center.Y + 24f * unit),
            "Release to equip", HorizontalAlignment.Center, labelW, smallSize, Colors.White);

        if (Entries.Count > 8)
            face.DrawString(small, new Vector2(center.X - labelW / 2f, center.Y + 48f * unit),
                $"L1 / R1  PAGE {_page + 1}/{(Entries.Count + 7) / 8}",
                HorizontalAlignment.Center, labelW, smallSize, UiTheme.TextDim);
    }
}
