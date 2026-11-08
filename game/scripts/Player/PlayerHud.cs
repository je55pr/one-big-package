using Godot;
using OBP.Runtime.Presentation;

namespace OBP.Game;

/// <summary>OBP's minimal gameplay HUD: two icon/count pairs and Nanotech spheres.</summary>
public partial class PlayerHud : Control
{
    private HBoxContainer _weaponRow = null!;
    private HBoxContainer _boltsRow = null!;
    private TextureRect _weaponIcon = null!;
    private TextureRect _boltsIcon = null!;
    private Label _ammoCount = null!;
    private Label _boltCount = null!;
    private HudHealth? _health;
    private long _epoch = -1;
    private long _revision = -1;

    public override void _Ready()
    {
        Name = "PlayerHud";
        MouseFilter = MouseFilterEnum.Ignore;
        AnchorRight = 1f;
        AnchorBottom = 1f;
        Resized += QueueRedraw;

        var weaponSlot = Passive(new MarginContainer
        {
            Name = "WeaponSlot",
            OffsetLeft = 24,
            OffsetTop = 20,
        });
        var boltSlot = Passive(new MarginContainer
        {
            Name = "BoltSlot",
            AnchorLeft = 1f,
            AnchorRight = 1f,
            OffsetLeft = -244,
            OffsetRight = -24,
            OffsetTop = 20,
        });
        AddChild(weaponSlot);
        AddChild(boltSlot);
        _weaponRow = Counter("WeaponAmmo", "WeaponIcon", "AmmoCount", out _weaponIcon, out _ammoCount);
        _boltsRow = Counter("Bolts", "BoltIcon", "BoltCount", out _boltsIcon, out _boltCount);
        weaponSlot.AddChild(_weaponRow);
        boltSlot.AddChild(_boltsRow);
        Visible = false;
    }

    public void Render(HudSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Epoch < _epoch || snapshot.Epoch == _epoch && snapshot.Revision < _revision) return;
        _epoch = snapshot.Epoch;
        _revision = snapshot.Revision;
        _health = snapshot.Health;

        _weaponRow.Visible = snapshot.CurrentWeapon is not null;
        if (snapshot.CurrentWeapon is { } weapon)
        {
            _weaponIcon.Texture = WeaponMenuIcons.ForWeapon(weapon.Weapon);
            _ammoCount.Visible = weapon.Ammo is not null;
            _ammoCount.Text = weapon.Ammo?.Current.ToString("N0") ?? string.Empty;
        }

        _boltsRow.Visible = snapshot.Bolts is not null;
        if (snapshot.Bolts is { } bolts)
        {
            _boltsIcon.Texture = WeaponMenuIcons.RegularBolt();
            _boltCount.Text = bolts.Balance.ToString("N0");
        }

        // Feedback events, status, prompts and oxygen never add HUD text or widgets.
        Visible = _health is not null || _weaponRow.Visible || _boltsRow.Visible;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_health is not { } health) return;
        int count = Math.Max(health.Current, health.Capacity ?? health.Current);
        if (count <= 0) return;
        float spacing = Math.Min(34f, Size.X * .36f / count);
        float radius = Math.Min(12f, spacing * .38f);
        float left = Size.X * .5f - (count - 1) * spacing * .5f;
        for (int i = 0; i < count; i++)
            DrawNanotechSphere(new Vector2(left + i * spacing, 42), radius, i < health.Current);
    }

    // Authored glossy spheres, not a claim about recovered retail HUD rendering.
    private void DrawNanotechSphere(Vector2 centre, float radius, bool filled)
    {
        DrawCircle(centre + new Vector2(0, 2), radius + 2, new Color(0, 0, 0, .55f), true, -1, true);
        if (filled)
            DrawCircle(centre, radius + 4, new Color(.1f, .7f, 1, .14f), true, -1, true);
        Color edge = filled ? new Color(.025f, .18f, .38f) : new Color(.025f, .055f, .08f, .8f);
        Color light = filled ? new Color(.35f, .9f, 1) : new Color(.08f, .15f, .2f, .8f);
        DrawCircle(centre, radius, edge, true, -1, true);
        for (int ring = 1; ring <= 16; ring++)
        {
            float t = ring / 16f;
            DrawCircle(centre + new Vector2(-radius * .22f, -radius * .25f) * t,
                radius * (1 - t * .8f), edge.Lerp(light, t), true, -1, true);
        }
        if (filled)
            DrawCircle(centre + new Vector2(-radius * .32f, -radius * .38f), radius * .18f,
                new Color(.9f, 1, 1, .9f), true, -1, true);
    }

    private static HBoxContainer Counter(string name, string iconName, string countName,
        out TextureRect icon, out Label count)
    {
        var row = Passive(new HBoxContainer { Name = name, Alignment = BoxContainer.AlignmentMode.End });
        row.AddThemeConstantOverride("separation", 12);
        icon = Passive(new TextureRect
        {
            Name = iconName,
            CustomMinimumSize = new Vector2(44, 44),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        });
        count = Passive(new Label
        {
            Name = countName,
            ThemeTypeVariation = "HudValue",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        });
        count.AddThemeColorOverride("font_color", Colors.White);
        count.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .85f));
        count.AddThemeConstantOverride("shadow_offset_x", 2);
        count.AddThemeConstantOverride("shadow_offset_y", 2);
        row.AddChild(icon);
        row.AddChild(count);
        return row;
    }

    private static T Passive<T>(T control) where T : Control
    {
        control.MouseFilter = MouseFilterEnum.Ignore;
        return control;
    }
}
