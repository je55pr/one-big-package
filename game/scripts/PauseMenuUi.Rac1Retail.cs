using Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Presentation;

namespace OBP.Game;

public sealed partial class PauseMenuUi
{
    public sealed record RetailItem(int NativeId, string Name, bool Owned, bool Equipped,
        ushort? Icon = null);
    public bool RetailEnabled { get; init; }
    public IReadOnlyList<RetailItem> Weapons { get; init; } = [];
    public IReadOnlyList<RetailItem> Gadgets { get; init; } = [];
    public IReadOnlyList<RetailItem> Items { get; init; } = [];
    /// <summary>Retail item_available[] bytes. Do not conflate with
    /// alternate_item_available[], which has independent progression.</summary>
    public IReadOnlyList<byte> NativePrimaryItemAvailability { get; init; } = [];
    /// <summary>Explicit alternate retail flags only, never inferred from weapon ownership.</summary>
    public IReadOnlyList<byte> NativeAlternateItemAvailability { get; init; } = [];
    public IReadOnlyList<RetailItem> QuickSlots { get; init; } = [];
    public event Action<Rac1WeaponId>? EquipRequested;
    /// <summary>Gameplay owner validates and persists an eight-slot native assignment.</summary>
    public Func<int, int, bool>? QuickSelectAssignRequested { get; set; }

    public Rac1RetailMenuPage RetailPage => _retailNavigation.Page;
    private readonly Rac1RetailMenuNavigation _retailNavigation = new();
    private VBoxContainer _retailColumn = null!;
    private Label _retailTitle = null!;
    private Label _retailBrand = null!;
    private PanelContainer _retailFrame = null!;
    private HSeparator _retailSeparator = null!;
    private Label _retailFooter = null!;
    private Control? _retailFocus;
    private Rac1HelpDeskView? _retailHelp;
    private readonly List<Control> _retailFocusOrder = [];
    private Control? _retailNativeRootOverlay;
    private Rac1MenuPanelMotion? _retailNativeRootMotion;
    private readonly List<Rac1RetailMenuButton> _retailNativeRootButtons = [];
    private double _retailTickFraction;
    private int _retailEquipmentPage;
    private readonly List<Rac1RetailEquipmentCell[]> _nativeGadgetGroups = [];
    private int _retailDevelopmentPage;
    private const int EquipmentPageSize = 9;

    private void BuildRetail()
    {
        SetProcess(true);
        var shade = new ColorRect
        {
            // The retail pause remains drawn over the actual level scene.
            // Any menu-stage world colour grading must be recovered separately.
            Color = Colors.Transparent,
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
        _retailFrame = frame;
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
        _retailSeparator = new HSeparator();
        column.AddChild(_retailSeparator);
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

        _retailFooter = new Label
        {
            Text = "START  Close    ·    CROSS / ENTER  Select    ·    CIRCLE / ESC  Back",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = UiTheme.TextDim,
        };
        column.AddChild(_retailFooter);
        _retailNativeRootOverlay = new Control
        {
            Name = "NativeProjectedRootPanels",
            AnchorRight = 1,
            AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_retailNativeRootOverlay);
        RebuildRetailPage();
    }

    public override void _Process(double delta)
    {
        if (UniversalEnabled && _universalSection == "Developer" &&
            _universalBody?.GetNodeOrNull<Label>("EffectsStatus") is { } effectStatus)
            effectStatus.Text = EffectStatusReader?.Invoke() ?? "Effects unavailable.";
        if (!RetailEnabled || !IsInsideTree()) return;
        RetainQuickSelectCandidateFocus();
        if (_retailNativeRootMotion is not null &&
            GetViewport().GetVisibleRect().Size != _nativeRootLastViewportSize)
            UpdateNativeRootPanels();
        _retailTickFraction += delta * 60d;
        int ticks = Math.Min(12, (int)_retailTickFraction);
        if (ticks <= 0) return;
        _retailTickFraction -= ticks;
        bool transitioning = _retailNavigation.IsTransitioning;
        _retailNavigation.Step(ticks);
        if (_retailNativeRootMotion is { RemainingUpdates: > 0 } motion)
        {
            for (int i = 0; i < ticks && motion.RemainingUpdates > 0; i++) motion.Tick();
            UpdateNativeRootPanels();
        }
        if (transitioning && !_retailNavigation.IsTransitioning) RebuildRetailPage();
        var current = GetViewport().GuiGetFocusOwner();
        if (current is not null && _retailFocusOrder.Contains(current))
        {
            _retailFocus = current;
            _retailNavigation.Focus(_retailFocusOrder.IndexOf(current), _retailFocusOrder.Count);
            // Native source text colours are configured by Rac1RetailMenuButton.
            // Avoid injecting a second, invented time-dependent highlight.
        }
    }

    private void NavigateRetail(Rac1RetailMenuPage target)
    {
        if (_retailNavigation.Navigate(target))
        {
            if (_retailNavigation.Page == Rac1RetailMenuPage.QuickSelect &&
                target != Rac1RetailMenuPage.QuickSelect)
                RestoreQuickSelectShoulderBindings();
            if (_retailNavigation.Page == Rac1RetailMenuPage.Root &&
                target != Rac1RetailMenuPage.Root && _retailNativeRootMotion is { } motion &&
                motion.RemainingUpdates == 0)
                motion.BeginTransition(Rac1MenuPanelPages.Page.OrdinaryPause, backwards: false);
            _retailEquipmentPage = 0;
            _retailDevelopmentPage = 0;
            _retailFocus = null;
            GetViewport().GuiReleaseFocus();
        }
    }

    private void RebuildRetailPage()
    {
        foreach (Node child in _retailColumn.GetChildren()) { _retailColumn.RemoveChild(child); child.QueueFree(); }
        if (_retailNativeRootOverlay is { } nativeOverlay)
        {
            foreach (Node child in nativeOverlay.GetChildren())
            {
                nativeOverlay.RemoveChild(child);
                child.QueueFree();
            }
            _retailNativeRootButtons.Clear();
            _retailNativeRootMotion = null;
            nativeOverlay.Visible = false;
        }
        _retailFocusOrder.Clear();
        _nativeGadgetGroups.Clear();
        _nativeQuickSelectGroups.Clear();
        _quickSelectSlotButtons = [];
        _retailHelp = null;
        Rac1RetailMenuPage page = _retailNavigation.Page;
        bool root = page == Rac1RetailMenuPage.Root;
        bool nativeProjectedRoot = root && _retailNativeRootOverlay is not null &&
            Rac1RetailUiAssets.NativeMenuPages is not null &&
            Rac1RetailUiAssets.NativeMenuPanels is not null;
        _retailFrame.Visible = !nativeProjectedRoot;
        _retailFrame.CustomMinimumSize = root ? Vector2.Zero : new Vector2(720, 560);
        bool development = page == Rac1RetailMenuPage.Development;
        // Native pages are projected menu panels over the actual game scene,
        // not one solid Godot "MenuCard". Keep OBP debug tools visibly distinct.
        if (development) _retailFrame.RemoveThemeStyleboxOverride("panel");
        else _retailFrame.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        _retailColumn.CustomMinimumSize = root ? Vector2.Zero : new Vector2(700, 410);
        _retailBrand.Visible = development;
        _retailTitle.Visible = !root;
        _retailSeparator.Visible = development;
        _retailFooter.Visible = development;
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
            case Rac1RetailMenuPage.Gadgets:
                if (!BuildNativeGadgetsPage()) BuildEquipmentPage(Gadgets, allowEquip: false);
                break;
            case Rac1RetailMenuPage.QuickSelect:
                if (!BuildNativeQuickSelectPage()) BuildEquipmentPage(QuickSlots, allowEquip: false);
                break;
            case Rac1RetailMenuPage.Items:
                if (!BuildNativeItemsPage()) BuildEquipmentPage(Items, allowEquip: false);
                break;
            case Rac1RetailMenuPage.Help: BuildHelpPage(); break;
            case Rac1RetailMenuPage.Options: BuildOptionsPage(); break;
            case Rac1RetailMenuPage.Development: BuildDevelopmentPage(); break;
        }
        MenuFocus.WireVertical(_retailFocusOrder);
        if (page == Rac1RetailMenuPage.Gadgets && _nativeGadgetGroups.Count > 0)
            WireNativeGadgetsFocus();
        else if (page == Rac1RetailMenuPage.QuickSelect && _nativeQuickSelectGroups.Count > 0)
            WireNativeQuickSelectFocus();
        else if (page is Rac1RetailMenuPage.Weapons or Rac1RetailMenuPage.Gadgets or
            Rac1RetailMenuPage.QuickSelect or Rac1RetailMenuPage.Items)
            WireRetailGrid(_retailFocusOrder.OfType<Rac1RetailEquipmentCell>().ToArray(),
                page == Rac1RetailMenuPage.Items
                    ? Rac1RetailUiAssets.NativeMenuPages?.NativeItemsGrid.Columns ?? 5
                    : Rac1RetailUiAssets.NativeMenuPages?.NativeWeaponsGrid.Columns ?? 5);
        RestoreRetailFocus();
    }

    private void BuildRetailRoot()
    {
        if (BuildNativeProjectedRoot()) return;
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
            AddRetailButton(Rac1RetailUiSkin.NativeText(id, fallback), () => NavigateRetail(page));
        // 2002 pause screenshot places the six rows above vertical centre.
        // Spacer positioning is screenshot-calibrated, NOT native UI geometry.
        _retailColumn.AddChild(new Control
        {
            CustomMinimumSize = new Vector2(0, 130),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
    }
    private void BuildEquipmentPage(IReadOnlyList<RetailItem> entries, bool allowEquip)
    {
        // FUN_0021d948 takes its dimensions, cell ordering, native icon
        // IDs and frame variants from the authored MenuScreen. The Weapons
        // page is now recovered directly from the ordinary root's linked
        // Veldin overlay page. Keep additional cross-game/host items on
        // separate, explicitly OBP overflow pages.
        var sourceWeapons = allowEquip ? Rac1RetailUiAssets.NativeMenuPages?.NativeWeaponsGrid : null;
        int columns = sourceWeapons?.Columns ?? 5;
        int pageSize = sourceWeapons?.Cells.Count ?? 15;
        if (sourceWeapons is not null)
        {
            var byId = entries.ToDictionary(item => item.NativeId);
            var nativeIds = sourceWeapons.Cells.Select(cell => cell.NativeId).ToHashSet();
            entries = sourceWeapons.Cells.Select(cell =>
            {
                if (byId.TryGetValue(cell.NativeId, out var item))
                    return item with { Icon = cell.Icon };
                var weapon = (Rac1WeaponId)cell.NativeId;
                string label = Enum.IsDefined(weapon)
                    ? Rac1WeaponProfiles.Name(weapon) : $"Native item {cell.NativeId}";
                return new RetailItem(cell.NativeId, label, false, false, cell.Icon);
            }).Concat(entries.Where(item => !nativeIds.Contains(item.NativeId))).ToArray();
        }
        int pageCount = Math.Max(1, (entries.Count + pageSize - 1) / pageSize);
        _retailEquipmentPage = Math.Clamp(_retailEquipmentPage, 0, pageCount - 1);

        var detail = new Label
        {
            Text = entries.Count == 0 ? "No items" : "",
            CustomMinimumSize = new Vector2(0, 40),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        detail.AddThemeFontOverride("font", Rac1RetailUiSkin.Font("regular"));
        detail.AddThemeColorOverride("font_color", Rac1RetailUiSkin.NativeNormal);

        var grid = new GridContainer
        {
            Columns = columns,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(690, 290),
        };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        _retailColumn.AddChild(grid);

        void Describe(RetailItem item)
        {
            string suffix = item.Equipped ? "  (ON)" : !item.Owned ? "  [LOCKED]" : "";
            detail.Text = item.Name + suffix;
        }

        int shown = 0;
        foreach (var entry in entries.Skip(_retailEquipmentPage * pageSize).Take(pageSize))
        {
            var item = entry;
            var cell = new Rac1RetailEquipmentCell
            {
                Name = $"RetailItem_{item.NativeId}",
                TooltipText = item.Name,
                NativeAnimationId = item.Icon,
                Owned = item.Owned,
                Equipped = item.Equipped,
                SuppressEquippedFrame = sourceWeapons is not null &&
                    (sourceWeapons.Flags & 0x20) != 0,
            };
            cell.FocusEntered += () =>
            {
                Describe(item);
                Rac1RetailMenuAudio.Play(0);
            };
            cell.Pressed += () =>
            {
                Rac1RetailMenuAudio.Play(1);
                if (!allowEquip || !Rac1RetailMenuNavigation.MayEquip(item.Owned)) return;
                // Only an equip request. The existing gameplay handler owns
                // campaign state and decides whether it is admitted.
                EquipRequested?.Invoke((Rac1WeaponId)item.NativeId);
            };
            grid.AddChild(cell);
            _retailFocusOrder.Add(cell);
            shown++;
        }
        for (int i = shown; i < pageSize; i++)
            grid.AddChild(new Control
            {
                CustomMinimumSize = new Vector2(112, 94),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });

        _retailColumn.AddChild(detail);
        if (shown > 0) Describe(entries[_retailEquipmentPage * pageSize]);

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

    /// <summary>
    /// The retail grid updater (FUN_0021b858) navigates up/down by the
    /// number of columns and wraps at edges when flag 0x8000 is absent.
    /// Preserve that native behaviour instead of Godot's nearest-node
    /// heuristic, which jumps diagonally through the icon grid.
    /// </summary>
    private static void WireRetailGrid(IReadOnlyList<Rac1RetailEquipmentCell> cells, int columns)
    {
        if (cells.Count == 0) return;
        for (int i = 0; i < cells.Count; i++)
        {
            int row = i / columns;
            int start = row * columns;
            int end = Math.Min(start + columns, cells.Count);
            int left = i == start ? end - 1 : i - 1;
            int right = i + 1 == end ? start : i + 1;
            int up = i - columns;
            if (up < 0)
            {
                int lastRowStart = ((cells.Count - 1) / columns) * columns;
                up = Math.Min(lastRowStart + i % columns, cells.Count - 1);
            }
            int down = i + columns;
            if (down >= cells.Count)
                down = Math.Min(i % columns, cells.Count - 1);
            cells[i].FocusNeighborLeft = cells[i].GetPathTo(cells[left]);
            cells[i].FocusNeighborRight = cells[i].GetPathTo(cells[right]);
            cells[i].FocusNeighborTop = cells[i].GetPathTo(cells[up]);
            cells[i].FocusNeighborBottom = cells[i].GetPathTo(cells[down]);
        }
    }

    private void ChangeRetailEquipmentPage(int delta)
    {
        _retailEquipmentPage += delta;
        _retailNavigation.Focus(0, _retailFocusOrder.Count);
        RebuildRetailPage();
    }

    private void BuildHelpPage()
    {
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
        AddRetailButton("OBP DEVELOPMENT TOOLS", () => NavigateRetail(Rac1RetailMenuPage.Development));
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
                if (FusionTestAvailable)
                    AddRetailButton(FusionTestEnabled ? "LEAVE THREE-GAME TEST" : "START THREE-GAME TEST", () => FusionTestRequested?.Invoke());
                AddRetailButton("REFILL ALL AMMO", () => RefillAllAmmoRequested?.Invoke(), !RefillAllAmmoAvailable);
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
            RetailPauseRoot = _retailNavigation.Page == Rac1RetailMenuPage.Root,
        };
        if (button.RetailPauseRoot)
        {
            button.CustomMinimumSize = new Vector2(365, 64);
            button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
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
