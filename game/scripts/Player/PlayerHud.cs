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
    private Rac1NanotechHudView _nanotech = null!;
    private long _epoch = -1;
    private long _revision = -1;

    public override void _Ready()
    {
        Name = "PlayerHud";
        MouseFilter = MouseFilterEnum.Ignore;
        AnchorRight = 1f;
        AnchorBottom = 1f;
        _nanotech = new Rac1NanotechHudView();
        AddChild(_nanotech);

        var weaponSlot = Passive(new MarginContainer
        {
            Name = "WeaponSlot",
            OffsetLeft = 24,
            OffsetTop = 20,
            OffsetRight = 300,
            OffsetBottom = 64,
        });
        var boltSlot = Passive(new MarginContainer
        {
            Name = "BoltSlot",
            AnchorLeft = 1f,
            AnchorRight = 1f,
            OffsetLeft = -244,
            OffsetRight = -24,
            OffsetTop = 20,
            OffsetBottom = 64,
        });
        AddChild(weaponSlot);
        AddChild(boltSlot);
        _weaponRow = Counter("WeaponAmmo", "WeaponIcon", "AmmoCount", out _weaponIcon, out _ammoCount);
        _weaponRow.Alignment = BoxContainer.AlignmentMode.Begin;
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
        _nanotech.Render(snapshot.Health);

        _weaponRow.Visible = snapshot.CurrentWeapon is not null;
        if (snapshot.CurrentWeapon is { } weapon)
        {
            _weaponIcon.Texture = WeaponMenuIcons.ForWeapon(weapon.Weapon);
            if (weapon.Ammo is { } ammo)
            {
                _ammoCount.Visible = true;
                _ammoCount.Text = ammo.Capacity is { } capacity
                    ? $"{ammo.Current:N0}/{capacity:N0}"
                    : $"{ammo.Current:N0}";
            }
            else
            {
                _ammoCount.Visible = false;
                _ammoCount.Text = string.Empty;
            }
        }

        _boltsRow.Visible = snapshot.Bolts is not null;
        if (snapshot.Bolts is { } bolts)
        {
            _boltsIcon.Texture = WeaponMenuIcons.RegularBolt();
            _boltCount.Text = bolts.Balance.ToString("N0");
        }

        // Feedback events, status, prompts and oxygen never add HUD text or widgets.
        Visible = snapshot.Health is not null || _weaponRow.Visible || _boltsRow.Visible;
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
