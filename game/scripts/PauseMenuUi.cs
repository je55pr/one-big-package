using Godot;
using OBP.RAC1.Gameplay;
using OBP.Core;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

public sealed partial class PauseMenuUi : CanvasLayer
{
    internal sealed record CreationBindings(
        ModalEquipmentPorts Equipment, Func<bool> Ordinary, Func<PlayerHost?> Player,
        Func<Rac1WeaponInventory> Inventory, Func<PlayerSessionNativeState?> Native,
        Func<PlayerAvatarPresentationHost> Avatar, Func<ActorWorldHost> Actors,
        Func<string?> ProfileStatus, Func<string?> SaveNotice,
        Func<string, EquipmentChangeResult> ResetActor, Func<GodotWorldResources?> Resources,
        Func<bool> Debug, Func<bool> Preview, Func<FusionWeaponHost> Fusion,
        Action Close, Action Lab, Action OpenSettings, Action<bool> SetDebug,
        Action SaveMovement, Action LoadMovement, Action<bool> RequestPreview,
        Action Worlds, Action Quit);

    internal static PauseMenuUi Create(CreationBindings bindings)
    {
        var equip = bindings.Equipment;
        var menu = new PauseMenuUi
        {
            ProcessMode = ProcessModeEnum.Always,
            RetailEnabled = !bindings.Ordinary() && bindings.Player()?.HasRetailPresentation == true && Rac1RetailUiAssets.IsLoaded,
            CatalogReader = bindings.Ordinary() ? equip.Catalog : null,
            SlotReader = bindings.Ordinary() ? equip.QuickSlots : null,
            CatalogEquip = equip.Equip,
            CatalogUnequip = equip.Unequip,
            CatalogGadget = equip.SetGadget,
            CatalogAssign = equip.AssignSlot,
            CatalogPreview = bindings.Avatar().CreateAppearancePreview,
            ActorStatusReader = bindings.Actors().ReadStatus,
            SharedProfileStatusReader = bindings.ProfileStatus,
            SaveNoticeReader = bindings.SaveNotice,
            ActorReset = bindings.ResetActor,
            EffectStatusReader = () =>
            {
                var resources = bindings.Resources();
                resources?.Effects?.Prepare();
                return resources?.Effects?.Status ?? resources?.EffectsFailure ?? "World effects are unavailable.";
            },
            EffectControl = operation => bindings.Resources()?.Effects?.Control(operation) ??
                new(false, bindings.Resources()?.EffectsFailure ?? "World effects are unavailable."),
            CatalogIcon = entry => entry.Definition.Item is { SourceGame: ObpSourceGame.Rac1 } item &&
                Rac1WeaponIconCatalog.Ordinary((Rac1WeaponId)item.NativeItemId) is { } icon
                ? Rac1RetailUiAssets.Sprite(icon.AnimationId, icon.Frame) : null,
            LabAvailable = bindings.Ordinary(),
            Weapons = Rac1WeaponProfiles.Additional.Select(profile => profile.Item)
                .Prepend(Rac1WeaponId.FirstRanged)
                .OrderBy(weapon => (int)weapon)
                .Select(weapon => new PauseMenuUi.RetailItem(
                    (int)weapon, Rac1WeaponProfiles.Name(weapon),
                    bindings.Inventory().Owns(weapon), bindings.Inventory().Equipped == weapon,
                    Rac1WeaponIconCatalog.Ordinary(weapon)?.AnimationId))
                .ToArray(),
            Gadgets = (Rac1RetailUiAssets.NativeMenuPages?.NativeGadgetsGrid.Groups
                .SelectMany(group => group.Cells).Select(cell => cell.NativeId) ??
                Enum.GetValues<Rac1TraversalEquipmentId>().Select(gadget => (int)gadget))
                .Select(id =>
                {
                    var gadget = (Rac1TraversalEquipmentId)id;
                    string name = Enum.IsDefined(gadget) ? gadget.ToString() switch
                    {
                        "HeliPack" => "Heli-Pack",
                        "ThrusterPack" => "Thruster-Pack",
                        "HydroPack" => "Hydro-Pack",
                        "OxygenMask" => "O2 Mask",
                        "Magneboots" => "Magneboots",
                        "Grindboots" => "Grindboots",
                        _ => gadget.ToString(),
                    } : $"Native item {id}";
                    return new PauseMenuUi.RetailItem(id, name,
                        bindings.Inventory().OwnsNativeItem(id), false);
                }).ToArray(),
            QuickSlots = (bindings.Native()?.Assignments.Capture().QuickSelect ?? bindings.Inventory().QuickSelect
                .Select(id => id == 0 ? (OBP.Runtime.Gameplay.SourceWeaponKey?)null : new OBP.Runtime.Gameplay.SourceWeaponKey(ObpSourceGame.Rac1, id)).ToArray()).Select((key, slot) =>
            {
                // The retained native menu only admits native icon/item IDs.
                // Foreign slots remain real in the OBP read model and wheel;
                // #259 owns their full pause-menu presentation.
                int id = key is { SourceGame: ObpSourceGame.Rac1 } native ? native.NativeItemId : 0;
                Rac1WeaponId weapon = (Rac1WeaponId)id;
                var icon = Rac1WeaponIconCatalog.Ordinary(weapon);
                return new PauseMenuUi.RetailItem(id,
                    key is { SourceGame: not ObpSourceGame.Rac1 } foreign ? $"{FusionWeaponHost.WeaponName(foreign)} [{SourceIdentityWire.FormatSource(foreign.SourceGame)}]" :
                    id == 0 ? $"Slot {slot + 1}: Empty" :
                    Enum.IsDefined(weapon) ? Rac1WeaponProfiles.Name(weapon) : $"Native item {id}",
                    id != 0 && bindings.Inventory().OwnsNativeItem(id),
                    id != 0 && bindings.Inventory().Equipped == weapon,
                    icon?.AnimationId);
            }).ToArray(),
            // Retail Items kind=0 reads item_available[]. Kind=1 reads
            // independent alternate_item_available[] progression flags,
            // which this campaign host does not yet model or invent.
            NativePrimaryItemAvailability = bindings.Inventory().Items,
            Items = Enumerable.Range(0, Rac1NativeInventoryLayout.ItemCount)
                .Where(id => bindings.Inventory().OwnsNativeItem(id) &&
                    !Enum.IsDefined((Rac1WeaponId)id) &&
                    !Enum.IsDefined((Rac1TraversalEquipmentId)id) &&
                    !(Rac1RetailUiAssets.NativeMenuPages?.NativeGadgetsGrid.Groups
                        .Any(group => group.Cells.Any(cell => cell.NativeId == id)) ?? false))
                .Select(id => new PauseMenuUi.RetailItem(id, $"Native item {id}", true, false))
                .ToArray(),
            DebugOutputEnabled = bindings.Debug(),
            MovementStatesAvailable = bindings.Player()?.CanSaveMovementState == true,
            WeaponPreviewEnabled = bindings.Preview(),
            FusionTestAvailable = bindings.Fusion().CanRunFixture,
            FusionTestEnabled = bindings.Fusion().TestActive,
            EquipmentStatus = bindings.Player() is { } activePlayer
                ? bindings.Fusion().TestActive ? "Three-game Veldin test · temporary inventory\nOriginal damage · OBP hand mounts / spread feedback\nHold Triangle / Q to select; Circle / E to fire."
                : $"Weapons: {(!bindings.Preview() ? "campaign inventory" : "full arsenal preview")}\nPack: {activePlayer.Rac1EquipmentLabel}\nSwingshot preview: {(activePlayer.Rac1DevelopmentSwingshotPreview ? "on" : "off")}"
                : string.Empty,
        };
        menu.EquipRequested += equip.SelectNativeWeapon;
        menu.FusionTestRequested += bindings.Fusion().ToggleTest;
        menu.LabRequested += () => { bindings.Close(); bindings.Lab(); };
        menu.QuickSelectAssignRequested = equip.AssignNativeSlot;
        menu.ResumeRequested += () => bindings.Close();
        menu.ControllerSettingsRequested += bindings.OpenSettings;
        menu.DebugOutputChanged += bindings.SetDebug;
        menu.SaveMovementStateRequested += bindings.SaveMovement;
        menu.LoadMovementStateRequested += bindings.LoadMovement;
        menu.WeaponPreviewRequested += () => { bindings.Close(); bindings.RequestPreview(bindings.Preview()); };
        menu.RefillPreviewRequested += () => { bindings.Close(); if (bindings.Preview()) bindings.RequestPreview(false); };
        menu.PackPreviewRequested += () => { bindings.Player()?.CycleRac1DevelopmentPack(); bindings.Close(); };
        menu.SwingshotPreviewRequested += () => { bindings.Player()?.ToggleRac1DevelopmentSwingshot(); bindings.Close(); };
        menu.WorldsRequested += bindings.Worlds;
        menu.QuitRequested += bindings.Quit;
        return menu;
    }

    public event Action? ResumeRequested;
    public event Action? ControllerSettingsRequested;
    public event Action? WorldsRequested;
    public event Action? QuitRequested;
    public event Action? SaveMovementStateRequested;
    public event Action? LoadMovementStateRequested;
    public event Action? WeaponPreviewRequested;
    public event Action? RefillPreviewRequested;
    public event Action? PackPreviewRequested;
    public event Action? SwingshotPreviewRequested;
    public event Action? FusionTestRequested;
    public bool FusionTestAvailable { get; init; }
    public bool FusionTestEnabled { get; init; }
    public event Action<bool>? DebugOutputChanged;
    public bool DebugOutputEnabled { get; set; }
    public string EquipmentStatus { get; init; } = string.Empty;
    public bool WeaponPreviewEnabled { get; init; }
    public bool MovementStatesAvailable { get; init; }
    private Label? _movementStatus;
    private Button? _resume;
    private readonly List<Control> _focusOrder = [];

    public void RestoreFocus()
    {
        if (UniversalEnabled) { RestoreUniversalFocus(); return; }
        if (RetailEnabled) { RestoreRetailFocus(); return; }
        MenuFocus.Restore(_resume, _focusOrder);
    }

    public void SetMovementStatus(string text)
    {
        if (_movementStatus is not null) _movementStatus.Text = text;
    }

    public override void _Ready()
    {
        Name = "PauseMenu";
        Layer = 250;
        ProcessMode = ProcessModeEnum.Always;
        if (UniversalEnabled) { BuildUniversal(); return; }
        if (RetailEnabled) { BuildRetail(); return; }

        AddChild(new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.72f),
            AnchorRight = 1,
            AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Stop,
        });

        var centre = new CenterContainer
        {
            AnchorRight = 1,
            AnchorBottom = 1,
        };
        AddChild(centre);

        var panel = new PanelContainer
        {
            ThemeTypeVariation = "MenuCard",
            CustomMinimumSize = new Vector2(460, 0),
        };
        centre.AddChild(panel);

        var scroll = new ScrollContainer
        {
            VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever,
            FollowFocus = true,
            CustomMinimumSize = new Vector2(460, Mathf.Clamp(GetViewport().GetVisibleRect().Size.Y - 100, 220, 660)),
        };
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        panel.AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(column);
        column.AddChild(new Label
        {
            Text = "PAUSED",
            ThemeTypeVariation = "HeaderLarge",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        column.AddChild(new Label
        {
            Text = "One Big Package",
            ThemeTypeVariation = "HeaderSmall",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var resume = MakeButton("RESUME");
        _resume = resume;
        var controller = MakeButton("CONTROLLER SETTINGS");
        var worlds = MakeButton("RETURN TO WORLDS");
        var quit = MakeButton("QUIT TO DESKTOP");

        resume.Pressed += () => ResumeRequested?.Invoke();
        controller.Pressed += () => ControllerSettingsRequested?.Invoke();
        worlds.Pressed += () => WorldsRequested?.Invoke();
        quit.Pressed += () => QuitRequested?.Invoke();

        column.AddChild(resume);
        column.AddChild(controller);
        _focusOrder.AddRange([resume, controller]);
        var debugOutput = new CheckButton
        {
            Name = "DebugOutputToggle",
            Text = "SHOW DEBUG OUTPUT",
            ButtonPressed = DebugOutputEnabled,
            CustomMinimumSize = new Vector2(0, 44),
            FocusMode = Control.FocusModeEnum.All,
            TooltipText = "Show world, movement and input diagnostics, development notices and inspection shortcuts.",
        };
        debugOutput.Toggled += enabled => DebugOutputChanged?.Invoke(enabled);
        column.AddChild(debugOutput);
        _focusOrder.Add(debugOutput);
        if (MovementStatesAvailable)
        {
            var weapons = MakeButton("TOGGLE WEAPON PREVIEW");
            var refill = MakeButton("REFILL PREVIEW AMMO");
            refill.Disabled = !WeaponPreviewEnabled;
            var pack = MakeButton("CYCLE MOVEMENT PACK");
            var swingshot = MakeButton("TOGGLE SWINGSHOT PREVIEW");
            weapons.Pressed += () => WeaponPreviewRequested?.Invoke();
            refill.Pressed += () => RefillPreviewRequested?.Invoke();
            pack.Pressed += () => PackPreviewRequested?.Invoke();
            swingshot.Pressed += () => SwingshotPreviewRequested?.Invoke();
            column.AddChild(new Label { Text = "TESTING EQUIPMENT", ThemeTypeVariation = "HeaderSmall" });
            column.AddChild(new Label { Text = EquipmentStatus, AutowrapMode = TextServer.AutowrapMode.WordSmart });
            if (FusionTestAvailable)
            {
                var fusion = MakeButton(FusionTestEnabled ? "LEAVE THREE-GAME WEAPON TEST" : "START THREE-GAME WEAPON TEST");
                fusion.Pressed += () => FusionTestRequested?.Invoke();
                column.AddChild(fusion); _focusOrder.Add(fusion);
            }
            foreach (var control in new[] { weapons, refill, pack, swingshot }) { column.AddChild(control); _focusOrder.Add(control); }
            var save = MakeButton("SAVE MOVEMENT STATE  (F12)");
            var load = MakeButton("LOAD MOVEMENT STATE  (SHIFT+F12)");
            save.TooltipText = "Save a stationary player position, facing, camera and movement equipment for reproducing a bug.";
            load.TooltipText = "Restore position, camera, inventory, defeated enemies, broken crates, cargo and transformations.";
            save.Pressed += () => SaveMovementStateRequested?.Invoke();
            load.Pressed += () => LoadMovementStateRequested?.Invoke();
            var movementButtons = new HBoxContainer();
            save.Text = "SAVE STATE  (F12)";
            load.Text = "LOAD  (SHIFT+F12)";
            save.SizeFlagsHorizontal = load.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            movementButtons.AddChild(save);
            movementButtons.AddChild(load);
            column.AddChild(movementButtons);
            _focusOrder.AddRange([save, load]);
            _movementStatus = new Label
            {
                Text = "Save and restore your test setup, including defeated enemies and broken crates.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(420, 0),
            };
            column.AddChild(_movementStatus);
        }
        column.AddChild(worlds);
        column.AddChild(quit);
        _focusOrder.AddRange([worlds, quit]);
        MenuFocus.WireVertical(_focusOrder);

        var hint = new Label
        {
            Text = "Start  Resume   ·   Cross  Select   ·   Triangle / Circle / Esc  Back",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = UiTheme.TextDim,
        };
        column.AddChild(hint);

        RestoreFocus();
    }

    public override void _Input(InputEvent input)
    {
        if (UniversalEnabled) HandleUniversalPreGuiInput(input);
        else HandleRetailQuickSelectInput(input);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (UniversalEnabled) { HandleUniversalInput(@event); return; }
        if (RetailEnabled) { HandleRetailInput(@event); return; }
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
            ResumeRequested?.Invoke();
        }
    }

    private static Button MakeButton(string text) => new()
    {
        Text = text,
        CustomMinimumSize = new Vector2(0, 48),
        FocusMode = Control.FocusModeEnum.All,
    };
}
