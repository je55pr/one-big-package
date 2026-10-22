using Godot;
using OBP.RAC1.Gameplay;

namespace OneBigPackage;

/// <summary>OBP selection presentation. Does not modify the native eight-slot inventory ring.</summary>
public sealed partial class WeaponQuickSelectUi : CanvasLayer
{
    public sealed record Entry(Rac1WeaponId Weapon, string Name, string Ammo);
    public IReadOnlyList<Entry> Entries { get; init; } = [];
    public Rac1WeaponId CurrentWeapon { get; init; }
    public event Action<Rac1WeaponId?>? Completed;
    public Rac1WeaponId? SelectedWeapon => SelectedIndex is { } index ? Entries[index].Weapon : null;
    public int? SelectedIndex { get; private set; }
    public bool NeutralTapCompleted { get; private set; }
    private ulong _openedAt;
    private bool _directed;
    private readonly Button[] _slots = new Button[8];
    private Label _pageLabel = null!;
    private int _page;

    public override void _Ready()
    {
        Name = "WeaponQuickSelect"; Layer = 240; ProcessMode = ProcessModeEnum.Always;
        _openedAt = Time.GetTicksMsec();
        AddChild(new ColorRect { Color = new Color(0, 0, 0, .65f), AnchorRight = 1, AnchorBottom = 1 });
        var center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        AddChild(center);
        var panel = new PanelContainer { ThemeTypeVariation = "MenuCard" }; center.AddChild(panel);
        var column = new VBoxContainer(); panel.AddChild(column);
        column.AddChild(new Label { Text = "QUICK SELECT", ThemeTypeVariation = "HeaderMedium", HorizontalAlignment = HorizontalAlignment.Center });
        _pageLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center }; column.AddChild(_pageLabel);
        var grid = new GridContainer { Columns = 3 }; column.AddChild(grid);
        int[] positions = [7, 0, 1, 6, -1, 2, 5, 4, 3];
        foreach (int slot in positions)
        {
            if (slot < 0)
            {
                grid.AddChild(new Label { Text = "HOLD TRIANGLE\nChoose with stick / D-pad\nRelease to equip", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                continue;
            }
            var button = new Button { CustomMinimumSize = new Vector2(172, 65), FocusMode = Control.FocusModeEnum.None };
            int captured = slot;
            button.Pressed += () => CompleteSlot(captured);
            _slots[slot] = button; grid.AddChild(button);
        }
        column.AddChild(new Label { Text = "Cross  Equip   ·   Circle  Cancel   ·   L1 / R1  Change page", HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiTheme.TextDim });
        int current = Entries.ToList().FindIndex(entry => entry.Weapon == CurrentWeapon);
        if (current >= 0) { _page = current / 8; SelectedIndex = current; }
        UpdateSlots();
    }

    public override void _Process(double delta)
    {
        float x = Input.GetActionStrength(RawGamepadInput.MoveRight) - Input.GetActionStrength(RawGamepadInput.MoveLeft);
        float y = Input.GetActionStrength(RawGamepadInput.MoveBack) - Input.GetActionStrength(RawGamepadInput.MoveForward);
        if (x * x + y * y < .25f) return;
        _directed = true;
        int slot = ((int)MathF.Floor((MathF.Atan2(x, -y) + MathF.PI / 8) / (MathF.PI / 4)) + 8) % 8;
        int index = _page * 8 + slot;
        if (index < Entries.Count && SelectedIndex != index) { SelectedIndex = index; UpdateSlots(); }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionReleased(ControllerBindings.QuickSelect))
        {
            NeutralTapCompleted = !_directed && Time.GetTicksMsec() - _openedAt < 250;
            Completed?.Invoke(SelectedWeapon);
        }
        else if (@event.IsActionPressed("ui_cancel") && !@event.IsActionPressed(ControllerBindings.QuickSelect))
            Completed?.Invoke(null);
        else if (@event.IsActionPressed("ui_accept"))
            Completed?.Invoke(SelectedWeapon);
        else if (@event.IsActionPressed("ui_focus_next") || @event.IsActionPressed("ui_focus_prev"))
        {
            _directed = true;
            int pages = Math.Max(1, (Entries.Count + 7) / 8);
            _page = (_page + (@event.IsActionPressed("ui_focus_next") ? 1 : pages - 1)) % pages;
            SelectedIndex = null;
            UpdateSlots();
        }
        else return;
        GetViewport().SetInputAsHandled();
    }

    private void CompleteSlot(int slot)
    {
        int index = _page * 8 + slot;
        if (index < Entries.Count) Completed?.Invoke(Entries[index].Weapon);
    }

    private void UpdateSlots()
    {
        _pageLabel.Text = $"{_page + 1} / {Math.Max(1, (Entries.Count + 7) / 8)}";
        for (int i = 0; i < 8; i++)
        {
            int index = _page * 8 + i;
            _slots[i].Text = index < Entries.Count ? Entries[index].Name + "\n" + Entries[index].Ammo : "—";
            _slots[i].Disabled = index >= Entries.Count;
            _slots[i].Modulate = SelectedIndex == index ? UiTheme.Accent : Colors.White;
        }
    }
}
