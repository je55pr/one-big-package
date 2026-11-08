using Godot;
using OBP.Fusion;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>Authored fixed 7x7 display; source-qualified catalog admission still owns every equip action.</summary>
public sealed partial class PauseMenuUi
{
    public Func<PlayerWeaponMenuFamily, Texture2D?>? WeaponFamilyIcon { get; init; }
    private readonly List<Button> _weaponCells = [];
    private string? _inspectedWeaponFamily;
    private Label _weaponName = null!, _weaponDescription = null!, _weaponResource = null!;
    private Button _weaponEquip = null!;
    private const int WeaponGridColumns = 7;

    private void BuildWeaponGrid()
    {
        var families = PlayerWeaponMenuRoster.Families;
        var catalog = CatalogReader!();
        _weaponCells.Clear();
        var layout = new HBoxContainer { Name = "ObpWeaponBoard", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        layout.AddThemeConstantOverride("separation", 18); _universalBody.AddChild(layout);
        var grid = new GridContainer { Name = "ObpWeaponGrid", Columns = WeaponGridColumns, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 5); grid.AddThemeConstantOverride("v_separation", 5); layout.AddChild(grid);
        if (_inspectedWeaponFamily is null)
            _inspectedWeaponFamily = families.FirstOrDefault(family => family.Recipient(catalog)?.State.Equipped == true)?.Id ?? families[0].Id;
        foreach (var family in families)
        {
            var entry = family.Recipient(catalog);
            bool available = entry?.Selectable == true;
            bool held = entry?.State.Equipped == true;
            var cell = new Button
            {
                Name = entry is null ? "ObpFamily_" + family.Id : "ObpItem_" + entry.Definition.Id.Replace('/', '_'),
                CustomMinimumSize = new(44, 60),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                FocusMode = Control.FocusModeEnum.All,
                TooltipText = family.Name + "\n" + (entry is null ? "Not implemented yet." : EquipmentNotice(entry) ?? "Press to equip."),
                AccessibilityName = family.Name,
            };
            cell.AddThemeStyleboxOverride("normal", WeaponCellStyle(held ? new(.12f, .25f, .25f) : new(.045f, .075f, .12f), held ? new(.4f, .9f, .75f) : new(.14f, .21f, .3f)));
            cell.AddThemeStyleboxOverride("hover", WeaponCellStyle(new(.1f, .17f, .24f), new(.35f, .6f, .7f)));
            cell.AddThemeStyleboxOverride("pressed", WeaponCellStyle(new(.1f, .24f, .25f), new(.45f, .9f, .8f)));
            cell.AddThemeStyleboxOverride("focus", WeaponCellStyle(new(0, 0, 0, 0), new(.6f, .95f, .85f)));
            var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); column.OffsetLeft = 3; column.OffsetRight = -3; column.OffsetTop = 3; column.OffsetBottom = -3;
            column.AddThemeConstantOverride("separation", 0); cell.AddChild(column);
            var icon = WeaponFamilyIcon?.Invoke(family);
            column.AddChild(new TextureRect
            {
                Texture = icon,
                CustomMinimumSize = new(0, 26),
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = Control.TextureFilterEnum.Linear,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Modulate = available ? Colors.White : new(.43f, .48f, .54f),
            });
            var label = new Label
            {
                Text = family.Name,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MaxLinesVisible = 2,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                CustomMinimumSize = new(0, 24),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Modulate = available ? new(.93f, .97f, 1) : new(.55f, .61f, .68f),
            };
            // The retail bitmap font has a fixed native size. Use the scalable
            // host face for these compact two-line captions so they cannot clip.
            label.AddThemeFontOverride("font", ThemeDB.FallbackFont);
            label.AddThemeFontSizeOverride("font_size", 10); column.AddChild(label);
            cell.FocusEntered += () => InspectWeaponFamily(family);
            cell.MouseEntered += () => InspectWeaponFamily(family);
            cell.Pressed += () => { InspectWeaponFamily(family); EquipWeaponFamily(family); };
            grid.AddChild(cell); _weaponCells.Add(cell); _universalFocus.Add(cell);
        }
        for (int i = families.Count; i < 49; i++)
        {
            var spare = new PanelContainer { Name = "ObpWeaponSpare", CustomMinimumSize = new(44, 60), MouseFilter = Control.MouseFilterEnum.Ignore };
            spare.AddThemeStyleboxOverride("panel", WeaponCellStyle(new(.025f, .04f, .065f), new(.08f, .12f, .18f))); grid.AddChild(spare);
        }
        var inspector = new VBoxContainer { Name = "ObpWeaponInspector", CustomMinimumSize = new(180, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = .45f };
        inspector.AddThemeConstantOverride("separation", 12); layout.AddChild(inspector);
        inspector.AddChild(new Label { Text = "WEAPONS  /  48 FAMILIES", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new(.5f, .8f, .72f) });
        _weaponName = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _weaponName.AddThemeFontSizeOverride("font_size", 23); inspector.AddChild(_weaponName);
        _weaponDescription = new Label { Name = "ObpWeaponDescription", AutowrapMode = TextServer.AutowrapMode.WordSmart }; inspector.AddChild(_weaponDescription);
        _weaponResource = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = UiTheme.TextDim }; inspector.AddChild(_weaponResource);
        _weaponEquip = MakeButton("EQUIP"); _weaponEquip.Name = "ObpWeaponEquip";
        _weaponEquip.Pressed += () => { if (families.FirstOrDefault(family => family.Id == _inspectedWeaponFamily) is { } family) EquipWeaponFamily(family); };
        inspector.AddChild(_weaponEquip); _universalFocus.Add(_weaponEquip);
        inspector.AddChild(new Label { Text = "D-pad / arrows  Browse\nCross / Enter  Equip\nL1 / R1  Sections", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = UiTheme.TextDim });
        var selected = families.First(family => family.Id == _inspectedWeaponFamily);
        InspectWeaponFamily(selected);
        _universalReturnFocus = _weaponCells[Array.IndexOf(families.ToArray(), selected)];
    }

    private static StyleBoxFlat WeaponCellStyle(Color background, Color border)
    {
        var style = new StyleBoxFlat { BgColor = background, BorderColor = border, CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5 };
        style.SetBorderWidthAll(1); return style;
    }

    private void InspectWeaponFamily(PlayerWeaponMenuFamily family)
    {
        if (_weaponName is null || !GodotObject.IsInstanceValid(_weaponName)) return;
        _inspectedWeaponFamily = family.Id;
        var entry = family.Recipient(CatalogReader!());
        _inspectedCatalogId = entry?.Definition.Id;
        _weaponName.Text = family.Name;
        _weaponDescription.Text = entry is null ? "Not implemented yet." : EquipmentNotice(entry) ??
            (entry.State.Equipped ? "Currently held." : entry.State.Selected ? "Selected in your loadout." : "Available in your loadout.");
        if (entry is not null && entry.Definition.DisplayName != family.Name) _weaponDescription.Text += "\n" + entry.Definition.DisplayName;
        _weaponResource.Text = entry?.State.Ammo is { } ammo ? $"AMMO  {ammo} / {entry.State.Capacity}" : "";
        if (WeaponFamilyIcon?.Invoke(family) is null) _weaponResource.Text += "\nIcon unavailable.";
        _weaponEquip.Disabled = entry?.Selectable != true;
        _weaponEquip.Text = entry?.State.Equipped == true ? "EQUIPPED" : "EQUIP";
        if (_weaponCells.Count > 0 && IsInsideTree()) WireWeaponGridFocus();
    }

    private void EquipWeaponFamily(PlayerWeaponMenuFamily family)
    {
        var entry = family.Recipient(CatalogReader!());
        if (_catalogActionPending || entry?.Selectable != true) return;
        _catalogActionPending = true;
        EquipmentChangeResult result;
        try { result = CatalogEquip?.Invoke(entry.Definition.Id) ?? new(false, "Equipment action is unavailable."); }
        catch (Exception error) { CatalogErrorReporter(error); result = new(false, "Equipment action failed."); }
        _catalogStatus.Text = result.Accepted ? "Weapon equipped." : (DebugOutputEnabled ? result.Reason : "This weapon could not be equipped.");
        Callable.From(() =>
        {
            if (!IsInsideTree() || IsQueuedForDeletion()) return;
            _catalogActionPending = false; RebuildUniversalPage();
        }).CallDeferred();
    }

    private void WireWeaponGridFocus()
    {
        if (_weaponCells.Count == 0 || _weaponEquip is null) return;
        int selected = Math.Max(0, PlayerWeaponMenuRoster.Families.ToList().FindIndex(family => family.Id == _inspectedWeaponFamily));
        var exit = MenuFocus.Available(_weaponEquip) ? (Control)_weaponEquip : _resume!;
        for (int i = 0; i < _weaponCells.Count; i++)
        {
            var cell = _weaponCells[i]; int start = i / WeaponGridColumns * WeaponGridColumns;
            int end = Math.Min(start + WeaponGridColumns - 1, _weaponCells.Count - 1);
            cell.FocusNeighborLeft = cell.GetPathTo(_weaponCells[i == start ? end : i - 1]);
            cell.FocusNeighborRight = cell.GetPathTo(_weaponCells[i == end ? start : i + 1]);
            cell.FocusNeighborTop = cell.GetPathTo(i >= WeaponGridColumns ? _weaponCells[i - WeaponGridColumns] : _universalTabs[0]);
            cell.FocusNeighborBottom = cell.GetPathTo(i + WeaponGridColumns < _weaponCells.Count ? _weaponCells[i + WeaponGridColumns] : exit);
        }
        foreach (var tab in _universalTabs) tab.FocusNeighborBottom = tab.GetPathTo(_weaponCells[selected]);
        _weaponEquip.FocusNeighborTop = _weaponEquip.FocusNeighborLeft = _weaponEquip.GetPathTo(_weaponCells[selected]);
    }
}
