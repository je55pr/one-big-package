using Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Presentation;

namespace OneBigPackage;

public sealed partial class PauseMenuUi
{
    public sealed record RetailItem(int NativeId, string Name, bool Owned, bool Equipped,
        ushort? Icon = null);
    public bool RetailEnabled { get; init; }
    public IReadOnlyList<RetailItem> Weapons { get; init; } = [];
    public IReadOnlyList<RetailItem> Gadgets { get; init; } = [];
    public IReadOnlyList<RetailItem> Items { get; init; } = [];
    public IReadOnlyList<RetailItem> QuickSlots { get; init; } = [];
    public event Action<Rac1WeaponId>? EquipRequested;

    public Rac1RetailMenuPage RetailPage => _retailNavigation.Page;
    private readonly Rac1RetailMenuNavigation _retailNavigation = new();
    private VBoxContainer _retailColumn = null!;
    private Label _retailTitle = null!;
    private Label _retailBrand = null!;
    private Control? _retailFocus;
    private Rac1HelpDeskView? _retailHelp;
    private readonly List<Control> _retailFocusOrder = [];
    private double _retailTickFraction;
    private int _retailEquipmentPage;
    private int _retailDevelopmentPage;
    private const int EquipmentPageSize = 9;

    private void BuildRetail()
    {
        SetProcess(true);
        var shade = new ColorRect
        {
            Color = new Color(0.015f, 0.022f, 0.055f, 0.80f),
            AnchorRight = 1,
            AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        AddChild(shade);

        var safe = new MarginContainer { AnchorRight = 1, AnchorBottom = 1 };
        safe.AddThemeConstantOverride("margin_left", 38);
        safe.AddThemeConstantOverride("margin_right", 38);
        safe.AddThemeConstantOverride("margin_top", 25);
        safe.AddThemeConstantOverride("margin_bottom", 28);
        AddChild(safe);

        var centred = new CenterContainer();
        centred.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        centred.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        safe.AddChild(centred);

        var frame = new PanelContainer
        {
            ThemeTypeVariation = "MenuCard",
            CustomMinimumSize = new Vector2(720, 560),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        centred.AddChild(frame);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        frame.AddChild(column);
        _retailBrand = new Label
        {
            Text = "RATCHET & CLANK",
            HorizontalAlignment = HorizontalAlignment.Center,
            ThemeTypeVariation = "HeaderLarge",
        };
        column.AddChild(_retailBrand);
        _retailTitle = new Label
        {
            Name = "RetailMenuTitle",
            HorizontalAlignment = HorizontalAlignment.Center,
            ThemeTypeVariation = "HeaderMedium",
        };
        column.AddChild(_retailTitle);
        var separator = new HSeparator();
        column.AddChild(separator);
        // No engine scrollbars. Retail pages are bounded and use explicit
        // previous/next controls when their data exceeds the visible grid.
        _retailColumn = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(700, 410),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _retailColumn.AddThemeConstantOverride("separation", 6);
        column.AddChild(_retailColumn);

        column.AddChild(new Label
        {
            Text = "START  Close    ·    CROSS / ENTER  Select    ·    CIRCLE / ESC  Back",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = UiTheme.TextDim,
        });
        RebuildRetailPage();
    }

    public override void _Process(double delta)
    {
        if (!RetailEnabled || !IsInsideTree()) return;
        _retailTickFraction += delta * 60d;
        int ticks = Math.Min(12, (int)_retailTickFraction);
        if (ticks <= 0) return;
        _retailTickFraction -= ticks;
        bool transitioning = _retailNavigation.IsTransitioning;
        _retailNavigation.Step(ticks);
        if (transitioning && !_retailNavigation.IsTransitioning) RebuildRetailPage();
        var current = GetViewport().GuiGetFocusOwner();
        if (current is not null && _retailFocusOrder.Contains(current))
        {
            _retailFocus = current;
            _retailNavigation.Focus(_retailFocusOrder.IndexOf(current), _retailFocusOrder.Count);
            // Host time pulse follows native frame-counter cadence in spirit,
            // but pixel brightness needs screenshot comparison.
            if (current is Button button && button.HasFocus())
                button.AddThemeColorOverride("font_focus_color",
                    Rac1RetailUiSkin.Cream.Lerp(Colors.White,
                        (Rac1RetailUiSkin.SelectionPulse(Time.GetTicksMsec()) - 0.72f) * 0.75f));
        }
    }

    private void NavigateRetail(Rac1RetailMenuPage target)
    {
        if (_retailNavigation.Navigate(target))
        {
            _retailEquipmentPage = 0;
            _retailDevelopmentPage = 0;
            _retailFocus = null;
            GetViewport().GuiReleaseFocus();
        }
    }

    private void RebuildRetailPage()
    {
        foreach (Node child in _retailColumn.GetChildren()) { _retailColumn.RemoveChild(child); child.QueueFree(); }
        _retailFocusOrder.Clear();
        _retailHelp = null;
        Rac1RetailMenuPage page = _retailNavigation.Page;
        _retailBrand.Visible = page != Rac1RetailMenuPage.Root;
        _retailTitle.Visible = page != Rac1RetailMenuPage.Root;
        _retailTitle.Text = page switch
        {
            Rac1RetailMenuPage.Weapons => Rac1RetailUiSkin.NativeText(20194, "Weapons"),
            Rac1RetailMenuPage.Gadgets => Rac1RetailUiSkin.NativeText(20195, "Gadgets"),
            Rac1RetailMenuPage.QuickSelect => Rac1RetailUiSkin.NativeText(20196, "Quick Select"),
            Rac1RetailMenuPage.Items => Rac1RetailUiSkin.NativeText(20197, "Items"),
            Rac1RetailMenuPage.Help => Rac1RetailUiSkin.NativeText(20198, "Help"),
            Rac1RetailMenuPage.Options => Rac1RetailUiSkin.NativeText(20199, "Options"),
            Rac1RetailMenuPage.Development => "OBP DEVELOPMENT TOOLS  •  NOT RETAIL",
            _ => "PAUSED",
        };

        switch (page)
        {
            case Rac1RetailMenuPage.Root: BuildRetailRoot(); break;
            case Rac1RetailMenuPage.Weapons: BuildEquipmentPage(Weapons, allowEquip: true); break;
            case Rac1RetailMenuPage.Gadgets: BuildEquipmentPage(Gadgets, allowEquip: false); break;
            case Rac1RetailMenuPage.QuickSelect: BuildEquipmentPage(QuickSlots, allowEquip: false); break;
            case Rac1RetailMenuPage.Items: BuildEquipmentPage(Items, allowEquip: false); break;
            case Rac1RetailMenuPage.Help: BuildHelpPage(); break;
            case Rac1RetailMenuPage.Options: BuildOptionsPage(); break;
            case Rac1RetailMenuPage.Development: BuildDevelopmentPage(); break;
        }
        MenuFocus.WireVertical(_retailFocusOrder);
        RestoreRetailFocus();
    }

    private void BuildRetailRoot()
    {
        var pages = new[]
        {
            (20194, "Weapons", Rac1RetailMenuPage.Weapons),
            (20195, "Gadgets", Rac1RetailMenuPage.Gadgets),
            (20196, "Quick Select", Rac1RetailMenuPage.QuickSelect),
            (20197, "Items", Rac1RetailMenuPage.Items),
            (20198, "Help", Rac1RetailMenuPage.Help),
            (20199, "Options", Rac1RetailMenuPage.Options),
        };
        foreach (var (id, fallback, page) in pages)
            AddRetailButton(Rac1RetailUiSkin.NativeText(id, fallback).ToUpperInvariant(), () => NavigateRetail(page));
        _retailColumn.AddChild(new HSeparator());
        _retailColumn.AddChild(new Label
        {
            Text = "OBP NAVIGATION  •  NOT RETAIL",
            ThemeTypeVariation = "HeaderSmall",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        AddRetailButton("RESUME", () => ResumeRequested?.Invoke());
        AddRetailButton("OBP DEVELOPMENT TOOLS  ▸", () => NavigateRetail(Rac1RetailMenuPage.Development));
    }

    private void BuildEquipmentPage(IReadOnlyList<RetailItem> entries, bool allowEquip)
    {
        if (entries.Count == 0)
            _retailColumn.AddChild(new Label
            {
                Text = "No supported equipment or item identities in this category.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                HorizontalAlignment = HorizontalAlignment.Center,
            });

        var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        _retailColumn.AddChild(grid);
        int pageCount = Math.Max(1, (entries.Count + EquipmentPageSize - 1) / EquipmentPageSize);
        _retailEquipmentPage = Math.Clamp(_retailEquipmentPage, 0, pageCount - 1);
        foreach (var entry in entries.Skip(_retailEquipmentPage * EquipmentPageSize).Take(EquipmentPageSize))
        {
            var item = entry;
            string state = item.Equipped ? "\n(ON)" : !item.Owned ? "\n[LOCKED]" : "";
            var button = new Rac1RetailMenuButton
            {
                Text = item.Name + state,
                Name = $"RetailItem_{item.NativeId}",
                CustomMinimumSize = new Vector2(208, 78),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                ClipText = true,
                FocusMode = Control.FocusModeEnum.All,
                TooltipText = item.Owned ? item.Name : $"{item.Name}: not owned",
            };
            if (item.Icon is { } id && Rac1RetailUiAssets.Sprite(id, 0) is { } icon)
            {
                button.Icon = icon;
                button.ExpandIcon = false;
            }
            if (!item.Owned) button.Modulate = new Color(.57f, .63f, .72f);
            button.Pressed += () =>
            {
                if (!allowEquip || !Rac1RetailMenuNavigation.MayEquip(item.Owned)) return;
                EquipRequested?.Invoke((Rac1WeaponId)item.NativeId);
                foreach (var other in grid.GetChildren().OfType<Button>())
                {
                    if (other.Name.ToString().StartsWith("RetailItem_"))
                        other.Text = other.Text.Replace("\n(ON)", "");
                }
                button.Text = item.Name + "\n(ON)";
            };
            button.FocusEntered += () => Rac1RetailMenuAudio.Play(0);
            button.Pressed += () => Rac1RetailMenuAudio.Play(1);
            grid.AddChild(button);
            _retailFocusOrder.Add(button);
        }
        _retailColumn.AddChild(new Label
        {
            Text = allowEquip ? "Select an owned weapon to equip. Locked items cannot be used."
                : "Inventory presentation only. Campaign ownership and progression stay source-owned.",
            Modulate = UiTheme.TextDim,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        if (pageCount > 1)
        {
            var nav = new HBoxContainer();
            nav.AddThemeConstantOverride("separation", 8);
            _retailColumn.AddChild(nav);
            if (_retailEquipmentPage > 0)
                AddRetailButton("◂ PREVIOUS", () => ChangeRetailEquipmentPage(-1), parent: nav);
            nav.AddChild(new Label
            {
                Text = $"  PAGE {_retailEquipmentPage + 1} / {pageCount}  ",
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (_retailEquipmentPage + 1 < pageCount)
                AddRetailButton("NEXT ▸", () => ChangeRetailEquipmentPage(1), parent: nav);
        }
        AddRetailButton("◂ BACK", () => NavigateRetail(Rac1RetailMenuPage.Root));
    }

    private void ChangeRetailEquipmentPage(int delta)
    {
        _retailEquipmentPage += delta;
        _retailNavigation.Focus(0, _retailFocusOrder.Count);
        RebuildRetailPage();
    }

    private void BuildHelpPage()
    {
        _retailColumn.AddChild(new Label
        {
            Text = "HELPDESK  •  Native headings and bitmap glyphs",
            ThemeTypeVariation = "HeaderSmall",
        });
        _retailHelp = new Rac1HelpDeskView
        {
            ExpandOnPrompt = false,
            CustomMinimumSize = new Vector2(620, 85),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _retailColumn.AddChild(_retailHelp);
        var categoryGrid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        categoryGrid.AddThemeConstantOverride("h_separation", 7);
        categoryGrid.AddThemeConstantOverride("v_separation", 4);
        _retailColumn.AddChild(categoryGrid);
        int[] categoryIds = [20209, 20210, 20211, 20212, 20213, 20214, 20215, 20216];
        foreach (int id in categoryIds)
        {
            string label = Rac1RetailUiSkin.NativeText(id, $"Help {id}");
            AddRetailButton(label, () =>
            {
                _retailHelp?.Prompt(label);
                _retailHelp?.Open();
            }, parent: categoryGrid);
        }
        _retailColumn.AddChild(new Label
        {
            Text = "Topic headings are native. Full contextual HelpDesk message IDs/audio gates are not yet wired.",
            Modulate = UiTheme.TextDim,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        AddRetailButton("◂ BACK", () => NavigateRetail(Rac1RetailMenuPage.Root));
    }

    private void BuildOptionsPage()
    {
        AddRetailButton("CONTROLLER SETTINGS", () => ControllerSettingsRequested?.Invoke());
        _retailColumn.AddChild(new HSeparator());
        _retailColumn.AddChild(new Label
        {
            Text = "OBP APPLICATION NAVIGATION  •  NOT RETAIL",
            ThemeTypeVariation = "HeaderSmall",
        });
        AddRetailButton("RETURN TO WORLDS", () => WorldsRequested?.Invoke());
        AddRetailButton("QUIT TO DESKTOP", () => QuitRequested?.Invoke());
        AddRetailButton("◂ BACK", () => NavigateRetail(Rac1RetailMenuPage.Root));
    }

    private void BuildDevelopmentPage()
    {
        _retailColumn.AddChild(new Label
        {
            Text = "These switches are OBP debugging and preview tools, not native Ratchet & Clank menu options.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = UiTheme.TextDim,
        });
        var debugOutput = new CheckButton
        {
            Name = "DebugOutputToggle",
            Text = "SHOW DEBUG OUTPUT",
            ButtonPressed = DebugOutputEnabled,
            CustomMinimumSize = new Vector2(0, 42),
        };
        debugOutput.Toggled += enabled => DebugOutputChanged?.Invoke(enabled);
        _retailColumn.AddChild(debugOutput);
        _retailFocusOrder.Add(debugOutput);

        if (MovementStatesAvailable)
        {
            if (_retailDevelopmentPage == 0)
            {
                _retailColumn.AddChild(new Label
                {
                    Text = EquipmentStatus,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                });
                AddRetailButton("TOGGLE WEAPON PREVIEW", () => WeaponPreviewRequested?.Invoke());
                AddRetailButton("REFILL PREVIEW AMMO", () => RefillPreviewRequested?.Invoke(), !WeaponPreviewEnabled);
                AddRetailButton("CYCLE MOVEMENT PACK", () => PackPreviewRequested?.Invoke());
                AddRetailButton("TOGGLE SWINGSHOT PREVIEW", () => SwingshotPreviewRequested?.Invoke());
                AddRetailButton("NEXT PAGE ▸", () => ChangeRetailDevelopmentPage(1));
            }
            else
            {
                AddRetailButton("SAVE STATE  (F12)", () => SaveMovementStateRequested?.Invoke());
                AddRetailButton("LOAD  (SHIFT+F12)", () => LoadMovementStateRequested?.Invoke());
                _movementStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
                _retailColumn.AddChild(_movementStatus);
                AddRetailButton("◂ PREVIOUS PAGE", () => ChangeRetailDevelopmentPage(-1));
            }
        }
        AddRetailButton("◂ BACK", () => NavigateRetail(Rac1RetailMenuPage.Root));
    }

    private void ChangeRetailDevelopmentPage(int delta)
    {
        _retailDevelopmentPage += delta;
        _retailNavigation.Focus(0, _retailFocusOrder.Count);
        RebuildRetailPage();
    }

    private void AddRetailButton(string label, Action action, bool disabled = false, Container? parent = null)
    {
        var button = new Rac1RetailMenuButton
        {
            Text = label,
            CustomMinimumSize = new Vector2(0, 43),
            FocusMode = Control.FocusModeEnum.All,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Disabled = disabled,
        };
        if (_retailNavigation.Page == Rac1RetailMenuPage.Root && label != "OBP DEVELOPMENT TOOLS  ▸" && label != "RESUME")
        {
            button.CustomMinimumSize = new Vector2(365, 49);
            button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            var normal = new StyleBoxFlat
            {
                BgColor = new Color(.035f, .052f, .11f, .96f),
                BorderColor = new Color(.42f, .46f, .48f),
                BorderWidthLeft = 2,
                BorderWidthTop = 2,
                BorderWidthRight = 2,
                BorderWidthBottom = 2,
            };
            button.AddThemeStyleboxOverride("normal", normal);
            button.AddThemeStyleboxOverride("focus", new StyleBoxFlat
            {
                BgColor = new Color(0, 0, 0, 0),
                BorderColor = new Color(.98f, .81f, .28f),
                BorderWidthLeft = 3,
                BorderWidthTop = 3,
                BorderWidthRight = 3,
                BorderWidthBottom = 3,
            });
        }
        button.FocusEntered += () => Rac1RetailMenuAudio.Play(0);
        button.Pressed += () => { Rac1RetailMenuAudio.Play(1); action(); };
        (parent ?? _retailColumn).AddChild(button);
        _retailFocusOrder.Add(button);
    }

    private void RestoreRetailFocus()
    {
        if (_retailNavigation.IsTransitioning) return;
        Control? preferred = _retailFocusOrder.Count > _retailNavigation.SelectedIndex
            ? _retailFocusOrder[_retailNavigation.SelectedIndex] : null;
        MenuFocus.Restore(preferred, _retailFocusOrder);
    }

    private void HandleRetailInput(InputEvent @event)
    {
        if (MovementStatesAvailable && @event is InputEventKey { Keycode: Key.F12, Pressed: true, Echo: false } key)
        {
            GetViewport().SetInputAsHandled();
            if (key.ShiftPressed) LoadMovementStateRequested?.Invoke();
            else SaveMovementStateRequested?.Invoke();
            return;
        }
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            if (_retailNavigation.Page == Rac1RetailMenuPage.Help && _retailHelp is { } help &&
                help.State.Current is Rac1HelpDeskPresentation.Phase.Text or Rac1HelpDeskPresentation.Phase.FadeIn)
            {
                help.Dismiss();
                return;
            }
            if (_retailNavigation.IsTransitioning) return;
            if (_retailNavigation.Page == Rac1RetailMenuPage.Root) ResumeRequested?.Invoke();
            else NavigateRetail(Rac1RetailMenuPage.Root);
        }
    }
}
