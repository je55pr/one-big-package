using Godot;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Presentation;

namespace OBP.Game;

/// <summary>
/// Isolated actual-Godot GUI regression: no game world or campaign changes.
/// Launch with res://diagnostics/Rac1RetailUiSmoke.tscn after --path, and
/// optionally --rac1-iso. Captures go outside tracked directories.
/// </summary>
public partial class Rac1RetailUiSmoke : Node
{
    public override void _Ready() => _ = Run();

    private async Task Run()
    {
        try
        {
            Engine.MaxFps = 60;
            GetTree().Root.Size = new Vector2I(960, 720);
            var args = CommandLineArgs.Parse(OS.GetCmdlineUserArgs());
            if (args.Rac1Iso is { } iso)
            {
                using var disc = new FileRandomAccessReader(iso);
                Rac1RetailUiAssets.Load(disc);
                UiTheme.Apply(GetTree().Root);
            }
            var itemFlags = new byte[37];
            itemFlags[30] = 1;
            itemFlags[34] = 1;
            itemFlags[4] = 1; // Not an alternate item index.
            var alternateFlags = new byte[128];
            alternateFlags[0] = 1; // Valid alternate index zero.
            alternateFlags[5] = 1;
            var sample = new PauseMenuUi
            {
                RetailEnabled = true,
                NativePrimaryItemAvailability = itemFlags,
                NativeAlternateItemAvailability = alternateFlags,
                MovementStatesAvailable = true,
                Weapons =
                [
                    new(10, "Bomb Glove", true, true, 0xEA6A),
                    new(15, "Blaster", false, false, 0xEA6F),
                    .. Enumerable.Range(0, 20).Select(i =>
                        new PauseMenuUi.RetailItem(60 + i, $"Test slot {i + 1}", false, false)),
                ],
                Gadgets = [new(12, "Swingshot", true, false, 0xEA6C)],
                QuickSlots = [new(10, "Bomb Glove", true, true, 0xEA6A)],
                Items = [new(31, "Native item 31", true, false)],
            };
            int equips = 0, options = 0, resumed = 0, quickAssignments = 0;
            var quickInventory = Rac1WeaponInventory.CreateOpeningLoadout();
            sample.QuickSelectAssignRequested = (slot, id) =>
            {
                quickAssignments++;
                return quickInventory.TryAssignQuickSelect(slot, id);
            };
            sample.EquipRequested += _ => equips++;
            sample.ControllerSettingsRequested += () => options++;
            sample.ResumeRequested += () => resumed++;
            UiPointerModality.MenuOpened();
            AddChild(sample);
            await Frames(4);
            Require(GetViewport().GuiGetFocusOwner() is Button { Text: "Weapons" }, "No initial Weapons focus");
            Require(Descendants(sample).OfType<Rac1RetailMenuButton>().Count(b => b.RetailPauseRoot) == 6,
                "Retail root must contain exactly six original menu choices");
            if (Rac1RetailUiAssets.IsLoaded)
            {
                var panels = Descendants(sample).OfType<Rac1RetailPanelSurface>().ToArray();
                Require(Rac1RetailUiAssets.NativeMenuPanels is
                { JointCount: 5 } recovered && recovered.Sequences.Count == 216,
                    "Original class-0x472 geometry/animation data did not load");
                Require(Rac1RetailUiAssets.NativeMenuPages is { } pages &&
                    pages[Rac1MenuPanelPages.Page.OrdinaryPause].ForSlot(0) == 14,
                    "Original 14-slot menu animation table did not load");
                Require(Rac1RetailUiAssets.CreateNativePanelMotion() is { } motion &&
                    motion.Pose(0).NativeSequence == 14 && motion.Pose(0).Frame == 6,
                    "Original panel motion track was not available to the Godot host");
                var nativeButtons = Descendants(sample).OfType<Rac1RetailMenuButton>()
                    .Where(b => b.Name.ToString().StartsWith("NativePanelSlot_", StringComparison.Ordinal))
                    .ToArray();
                Require(nativeButtons.Length == 6, "Projected class-0x472 geometry did not replace fixed root rows");
                Require(Rac1RetailUiAssets.NativeRootFont is not null &&
                    Rac1RetailUiAssets.NativeRootGlyphAtlas() is not null,
                    "Source R&C1 large-font glyph atlas was not decoded");
                Require(nativeButtons.All(button =>
                    button.GetChildren().OfType<Rac1RetailRootText>().Count() == 1 &&
                    button.GetThemeColor("font_color").A == 0),
                    "Original pause glyphs must replace Godot-shaped Button text exactly once");
                var sourceLabels = Rac1RetailUiAssets.NativeMenuPages!.NativeRootEntries;
                Require(sourceLabels.Count == 7 && sourceLabels[6].RequiresNativeAdmission,
                    "Retail ordinary root lost its conditional seventh text screen");
                for (int slot = 0; slot < nativeButtons.Length; slot++)
                {
                    var entry = sourceLabels[slot];
                    Require(entry.Slot == slot && !entry.RequiresNativeAdmission &&
                        nativeButtons[slot].Text == Rac1RetailUiAssets.Text(entry.TextId, "<missing native text>"),
                        $"Root label at slot {slot} is not the authored retail text entry");
                }
                var sourceTrack = Rac1RetailUiAssets.CreateNativePanelMotion()!;
                sourceTrack.BeginTransition(Rac1MenuPanelPages.Page.OrdinaryPause, backwards: true);
                for (int tick = 0; tick < 12; tick++) sourceTrack.Tick();
                var viewportSize = GetViewport().GetVisibleRect().Size;
                // Verify all six real Godot controls use the discrete
                // native x/y+1 and independently truncated width/height.
                // A 2-pixel tolerance would conceal the original off-by-one.
                for (int slot = 0; slot < nativeButtons.Length; slot++)
                {
                    var projected = Rac1MenuPanelCamera.ProjectPanel(sourceTrack.Pose(slot),
                        new System.Numerics.Vector2(viewportSize.X, viewportSize.Y));
                    var button = nativeButtons[slot];
                    Require(Mathf.Abs(button.Position.X - projected.X) < .01f &&
                        Mathf.Abs(button.Position.Y - projected.Y) < .01f &&
                        Mathf.Abs(button.Size.X - projected.Width) < .01f &&
                        Mathf.Abs(button.Size.Y - projected.Height) < .01f,
                        $"Native pixel raster bounds differ from Godot panel placement at slot {slot}");
                }
                Require(nativeButtons[0].Position.Y < nativeButtons[1].Position.Y,
                    "Source pause panel positions are out of order");
                Require(panels.Length == 6, "Native panel compositing must cover all six pause rows");
                Require(panels.All(panel => panel.Material is ShaderMaterial { Shader: not null }),
                    "Original retail panel shader did not load");
            }
            Require(!Descendants(sample).OfType<Label>().Any(l => l.Text.Contains("OBP NAVIGATION", StringComparison.Ordinal)),
                "OBP chrome leaked into retail pause root");
            await Capture("root");
            if (Rac1RetailUiAssets.IsLoaded && DisplayServer.GetName() != "headless")
            {
                // The original ELF panel-clear rectangle must keep all six
                // text strips legible against a deliberately bright scene.
                var brightWorld = new ColorRect
                {
                    Name = "UiContrastStressWorld",
                    Color = new Color(1, 1, .88f),
                    Size = GetViewport().GetVisibleRect().Size,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                AddChild(brightWorld);
                MoveChild(brightWorld, 0);
                await Frames(4);
                await Capture("root-bright");
                var image = GetViewport().GetTexture().GetImage();
                var outside = image.GetPixel(15, 15);
                Require(outside.R > .85f && outside.G > .85f,
                    "The bright world fixture was not rendered underneath the pause menu");
                foreach (var button in Descendants(sample).OfType<Rac1RetailMenuButton>()
                    .Where(b => b.Name.ToString().StartsWith("NativePanelSlot_", StringComparison.Ordinal)))
                {
                    var px = (Vector2I)(button.Position +
                        new Vector2(button.Size.X * .10f, button.Size.Y * .48f));
                    var inside = image.GetPixel(px.X, px.Y);
                    Require(inside.R < .26f && inside.G < .26f && inside.B < .3f,
                        $"Original panel clear pass failed over bright background, {button.Name}: {inside}");
                }
                brightWorld.QueueFree();
                await Frames(3);
            }
            Require(!Descendants(sample).OfType<ScrollContainer>().Any(), "Root retained forbidden ScrollContainer");
            ApplicationPointer.MouseMode = Input.MouseModeEnum.Visible;
            await Press(JoyButton.DpadDown);
            Require(UiPointerModality.ControllerWasLastInput, "Controller input was not recorded");
            if (DisplayServer.GetName() != "headless")
                Require(ApplicationPointer.MouseMode == Input.MouseModeEnum.Hidden, "Controller input failed to hide cursor");
            await Frames(30); // Allow pause-open warp suppression to expire.
            Input.ParseInputEvent(new InputEventMouseMotion
            {
                Relative = new Vector2(12, 0),
                Position = new Vector2(200, 180),
            });
            await Frames(2);
            Require(!UiPointerModality.ControllerWasLastInput, "Mouse movement did not restore pointer modality");
            if (DisplayServer.GetName() != "headless")
                Require(ApplicationPointer.MouseMode == Input.MouseModeEnum.Visible, "Physical mouse movement failed to show cursor");
            Require(GetViewport().GuiGetFocusOwner() is Button { Text: "Gadgets" },
                "D-pad navigation did not move from Weapons to Gadgets");
            await Press(JoyButton.DpadUp);
            Require(GetViewport().GuiGetFocusOwner() is Button { Text: "Weapons" },
                $"D-pad Up did not restore Weapons: focus={GetViewport().GuiGetFocusOwner()?.Name}," +
                $" text={(GetViewport().GuiGetFocusOwner() as Button)?.Text}," +
                $" controllerLast={UiPointerModality.ControllerWasLastInput}");
            await Press(JoyButton.A);
            await Frames(25);
            Require(sample.RetailPage == Rac1RetailMenuPage.Weapons, $"Cross did not enter Weapons: page={sample.RetailPage}, focus={(GetViewport().GuiGetFocusOwner() as Button)?.Text}");
            var buttons = Descendants(sample).OfType<Button>().Where(b => b.Name.ToString().StartsWith("RetailItem_")).ToArray();
            Require(buttons.Length == 15 && buttons[1] is Rac1RetailEquipmentCell { Owned: false },
                "Native inventory paging or ownership state missing");
            if (Rac1RetailUiAssets.NativeMenuPages is { } nativePages)
            {
                var source = nativePages.NativeWeaponsGrid;
                Require(source.Columns == 5 && source.Rows == 3 &&
                    buttons.Select(button => button.Name.ToString()).SequenceEqual(
                        source.Cells.Select(cell => $"RetailItem_{cell.NativeId}")),
                    "Godot Weapons page does not preserve all fifteen original item grid slots");
                Require(((Rac1RetailEquipmentCell)buttons[0]).NativeAnimationId ==
                    source.Cells[0].Icon, "Native weapon icons did not come from source item cells");
                Require(((Rac1RetailEquipmentCell)buttons[0]).SuppressEquippedFrame,
                    "Weapons grid flag 0x20 must suppress the equipped-icon frame variant");
            }
            Require(!Descendants(sample).OfType<ScrollContainer>().Any(), "Retail UI unexpectedly contains Godot scrollbars");
            buttons[0].GrabFocus(); await Frames(2);
            await Press(JoyButton.DpadRight);
            Require(GetViewport().GuiGetFocusOwner() == buttons[1], "R&C1 five-column D-pad right navigation failed");
            await Press(JoyButton.DpadDown);
            Require(GetViewport().GuiGetFocusOwner() == buttons[6],
                $"R&C1 grid D-pad down failed: actual={GetViewport().GuiGetFocusOwner()?.Name}, expected={buttons[6].Name}, bottomPath={buttons[1].FocusNeighborBottom}, btn1={buttons[1].GetPath()}");
            buttons[1].GrabFocus(); await Frames(2); await Press(JoyButton.A);
            Require(equips == 0, "Unowned weapon sent an equip request");
            buttons[0].GrabFocus(); await Frames(2); await Press(JoyButton.A);
            Require(equips == 1, "Owned weapon ignored Cross");
            await Capture("weapons");
            await FocusAndPress(sample, "NEXT ▸");
            await Frames(4);
            buttons = Descendants(sample).OfType<Button>().Where(b => b.Name.ToString().StartsWith("RetailItem_")).ToArray();
            if (Rac1RetailUiAssets.NativeMenuPages is not null)
            {
                Require(buttons.Length == 15 && buttons[0].TooltipText == "Test slot 1",
                    "Source Weapons page did not put host-only items on a separate overflow page");
                await Capture("weapons-page2");
                await FocusAndPress(sample, "NEXT", contains: true);
                await Frames(4);
                buttons = Descendants(sample).OfType<Button>()
                    .Where(b => b.Name.ToString().StartsWith("RetailItem_")).ToArray();
                Require(buttons.Length == 5 && buttons[0].TooltipText == "Test slot 16",
                    "Final overflow page did not retain the host-only inventory entries");
            }
            else
            {
                Require(buttons.Length == 7 && buttons[0].TooltipText == "Test slot 14",
                    "Fallback second inventory page lost six host entries");
                await Capture("weapons-page2");
            }

            await Keyboard(Key.Escape); await Frames(25);
            Require(sample.RetailPage == Rac1RetailMenuPage.Root, "Keyboard Escape did not navigate back");
            await Open(sample, "Gadgets", Rac1RetailMenuPage.Gadgets);
            if (Rac1RetailUiAssets.NativeMenuPages is { } gadgetPages)
            {
                var source = gadgetPages.NativeGadgetsGrid;
                var groups = Descendants(sample).OfType<GridContainer>()
                    .Where(grid => grid.Name.ToString().StartsWith("NativeGadgetGroup_", StringComparison.Ordinal))
                    .ToArray();
                Require(groups.Length == 4 &&
                    groups.Select(group => group.Columns).SequenceEqual(source.Groups.Select(group => group.Columns)) &&
                    groups.SelectMany(group => group.GetChildren().OfType<Rac1RetailEquipmentCell>())
                        .Select(cell => cell.Name.ToString())
                        .SequenceEqual(source.Groups.SelectMany(group => group.Cells)
                            .Select(cell => $"RetailItem_{cell.NativeId}")),
                    "Gadgets did not use original four source-linked grid groups");
                Require(GetViewport().GuiGetFocusOwner()?.Name.ToString() == "RetailItem_2",
                    "Original Gadgets page must initially focus the Heli-Pack group");
                await Press(JoyButton.DpadUp);
                Require(GetViewport().GuiGetFocusOwner()?.Name.ToString() == "RetailItem_32",
                    "Gadget Up failed to cross from group 3 to previous group 2 last row");
                await Press(JoyButton.DpadDown);
                Require(GetViewport().GuiGetFocusOwner()?.Name.ToString() == "RetailItem_2",
                    "Gadget Down failed to enter the originally focused group");
                await Press(JoyButton.DpadRight);
                Require(GetViewport().GuiGetFocusOwner()?.Name.ToString() == "RetailItem_3",
                    "Gadget right did not follow source three-column group");
                await Press(JoyButton.DpadDown);
                Require(GetViewport().GuiGetFocusOwner()?.Name.ToString() == "RetailItem_5",
                    "Gadget group 3 to group 9 focus transfer failed");
                await Press(JoyButton.DpadDown);
                Require(GetViewport().GuiGetFocusOwner()?.Name.ToString() == "RetailItem_28",
                    "Gadget group 9 to narrower group 10 focus transfer failed");
                await Press(JoyButton.DpadUp);
                Require(GetViewport().GuiGetFocusOwner()?.Name.ToString() == "RetailItem_5",
                    "Gadget group 10 upward transfer failed");
                Require(equips == 1, "Inspecting a gadget changed the weapon selection");
            }
            await Capture("gadgets");
            await Keyboard(Key.Escape); await Frames(25);
            await Open(sample, "Quick Select", Rac1RetailMenuPage.QuickSelect);
            if (Rac1RetailUiAssets.NativeMenuPages is { } quickPages)
            {
                var groups = Descendants(sample).OfType<GridContainer>()
                    .Where(grid => grid.Name.ToString().StartsWith("NativeQuickCandidates_", StringComparison.Ordinal))
                    .ToArray();
                var source = quickPages.NativeQuickSelectPage;
                Require(groups.Length == 2 &&
                    groups.Select(group => group.Columns).SequenceEqual(source.Groups.Select(group => group.Columns)) &&
                    groups.SelectMany(group => group.GetChildren().OfType<Rac1RetailEquipmentCell>())
                        .Select(cell => cell.Name.ToString())
                        .SequenceEqual(source.Groups.SelectMany(group => group.Cells)
                            .Select(cell => $"QuickCandidate_{cell.NativeId}")),
                    "Quick Select did not preserve its 15 weapon and 6 gadget candidates");
                var slotButtons = Descendants(sample).OfType<Button>()
                    .Where(button => button.Name.ToString().StartsWith("QuickSlot_", StringComparison.Ordinal))
                    .OrderBy(button => button.Name.ToString()).ToArray();
                Require(slotButtons.Length == 8 && slotButtons[0].Text == "1: 10" &&
                    slotButtons.Skip(1).All(button => button.Text.EndsWith("-", StringComparison.Ordinal)),
                    "Quick Select conflated the six gadget candidates with eight save-backed slots");
                Require(GetViewport().GuiGetFocusOwner()?.Name.ToString() == "QuickCandidate_10",
                    "Quick Select must initially focus the first original weapon candidate");
                await Press(JoyButton.DpadUp);
                Require(GetViewport().GuiGetFocusOwner()?.Name.ToString() == "QuickCandidate_32",
                    "Quick Select Up from 5-column group failed 5-to-3 index shift");
                await Press(JoyButton.DpadDown);
                Require(GetViewport().GuiGetFocusOwner()?.Name.ToString() == "QuickCandidate_16",
                    "Quick Select Down from 3-column group failed 3-to-5 index shift");
                await Press(JoyButton.A);
                Require(quickAssignments == 0, "Quick Select accepted an unowned weapon candidate");
                var bomb = Descendants(sample).OfType<Rac1RetailEquipmentCell>()
                    .Single(cell => cell.Name.ToString() == "QuickCandidate_10");
                bomb.GrabFocus(); await Frames(2);
                Require(!InputMap.ActionGetEvents("ui_focus_next").OfType<InputEventJoypadButton>()
                    .Any(ev => ev.ButtonIndex == JoyButton.RightShoulder),
                    "Quick Select must own R1 without Godot also cycling weapon focus");
                await Press(JoyButton.RightShoulder);
                Require(GetViewport().GuiGetFocusOwner() == bomb,
                    "R1 slot cycling unexpectedly moved the source weapon candidate focus");
                await Press(JoyButton.A);
                Require(quickAssignments == 1 && quickInventory.QuickSelect[0] == 0 &&
                    quickInventory.QuickSelect[1] == 10 &&
                    slotButtons[0].Text == "1: -" && slotButtons[1].Text == "2: 10",
                    $"Native Quick Select assignment mismatch: events={quickAssignments}, slots={string.Join(",", quickInventory.QuickSelect)}, labels={slotButtons[0].Text}/{slotButtons[1].Text}, focus={GetViewport().GuiGetFocusOwner()?.Name}");
                Require(equips == 1 && quickInventory.CurrentItemId == 10,
                    "Quick Select assignment unexpectedly changed the held weapon or equip handler");
            }
            await Capture("quickselect");
            await Keyboard(Key.Escape); await Frames(25);
            if (Rac1RetailUiAssets.NativeMenuPages is not null)
                Require(InputMap.ActionGetEvents("ui_focus_next").OfType<InputEventJoypadButton>()
                    .Any(ev => ev.ButtonIndex == JoyButton.RightShoulder),
                    "Leaving Quick Select did not restore the user's gamepad focus mapping");
            await Open(sample, "Items", Rac1RetailMenuPage.Items);
            if (Rac1RetailUiAssets.NativeMenuPages is { } itemPages)
            {
                var native = itemPages.NativeItemsGrid;
                var itemGrid = Descendants(sample).OfType<GridContainer>()
                    .Single(grid => grid.Name.ToString() == "NativeItemsGrid");
                var cells = itemGrid.GetChildren().OfType<Rac1RetailEquipmentCell>().ToArray();
                Require(itemGrid.Columns == 3 && cells.Length == 9,
                    "Retail Items must contain exactly nine source-authored 3x3 cells");
                for (int index = 0; index < cells.Length; index++)
                {
                    var source = native.Cells[index];
                    var cell = cells[index];
                    Require(cell.Name.ToString() == $"NativeItemsCell_{index}" &&
                        cell.NativeAnimationId == source.Icon && cell.NativeFrame == source.Frame &&
                        cell.Owned == native.IsAvailable(source, itemFlags, alternateFlags),
                        $"Items cell {index} lost its native icon frame or independent availability");
                }
                Require(!cells[3].Owned && cells[4].Owned && cells[6].Owned &&
                    cells[0].Owned && !cells[1].Owned,
                    "Kind=1 availability must not alias kind=0 inventory IDs, including alternate index zero");
                Require(cells[2].NativeAnimationId == cells[3].NativeAnimationId &&
                    cells[2].NativeFrame != cells[3].NativeFrame,
                    "Multiple Items icons share the same retail animation with distinct authored frames");
                Require(GetViewport().GuiGetFocusOwner() == cells[0],
                    "Items must initially focus the first source grid cell");
                await Press(JoyButton.DpadRight);
                Require(GetViewport().GuiGetFocusOwner() == cells[1],
                    "Items D-pad right must advance one column in its 3x3 grid");
                await Press(JoyButton.DpadDown);
                Require(GetViewport().GuiGetFocusOwner() == cells[4],
                    "Items D-pad down must move three cells, not the Weapons five-column stride");
                int beforeEquip = equips;
                await Press(JoyButton.A);
                cells[0].GrabFocus(); await Frames(2);
                await Press(JoyButton.A);
                Require(equips == beforeEquip,
                    "Original Items grid flag 1 must prevent Cross from equipping anything");
                Require(!Descendants(sample).OfType<ScrollContainer>().Any(),
                    "Original Items page introduced a host scrollbar");
            }
            await Capture("items");
            await Keyboard(Key.Escape); await Frames(25);

            await Open(sample, "Help", Rac1RetailMenuPage.Help);
            await FocusAndPress(sample, "Controls");
            await Frames(18);
            var help = Descendants(sample).OfType<Rac1HelpDeskView>().First();
            Require(help.State.Current == Rac1HelpDeskPresentation.Phase.Text, $"HelpDesk expansion not visible: {help.State.Current}");
            await Capture("helpdesk");
            await Keyboard(Key.Escape); await Frames(12);
            await Keyboard(Key.Escape); await Frames(25);
            Require(sample.RetailPage == Rac1RetailMenuPage.Root, "HelpDesk Back did not return to root");

            await Open(sample, "Options", Rac1RetailMenuPage.Options);
            await FocusAndPress(sample, "CONTROLLER SETTINGS");
            Require(options == 1, "Options did not forward settings event");
            await Capture("options");
            await Open(sample, "OBP DEVELOPMENT TOOLS", Rac1RetailMenuPage.Development, contains: true);
            Require(Descendants(sample).OfType<CheckButton>().Any(c => c.Text == "SHOW DEBUG OUTPUT"),
                "Development controls disappeared");
            await Capture("development");
            await Keyboard(Key.Escape); await Frames(25);
            await Keyboard(Key.Escape); await Frames(2);
            Require(resumed == 1, "Escape did not resume from retail root");
            GD.Print("[rac1-retail-ui-smoke] keyboard, joypad, pages, lockouts, HelpDesk and developer separation PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PrintErr("[rac1-retail-ui-smoke] FAIL " + ex);
            GetTree().Quit(1);
        }
    }

    private async Task Open(PauseMenuUi menu, string label, Rac1RetailMenuPage page, bool contains = false)
    {
        await FocusAndPress(menu, label, contains);
        await Frames(25);
        Require(menu.RetailPage == page, "Expected page " + page);
    }

    private async Task FocusAndPress(PauseMenuUi menu, string label, bool contains = false)
    {
        var match = Descendants(menu).OfType<Button>().First(b =>
            contains ? b.Text.Contains(label, StringComparison.Ordinal) : b.Text == label);
        match.GrabFocus();
        await Frames(3);
        await Press(JoyButton.A);
    }

    private async Task Press(JoyButton button)
    {
        Input.ParseInputEvent(new InputEventJoypadButton { Device = 0, ButtonIndex = button, Pressed = true });
        // The menu repeat period is real time (350ms); three long frames
        // under concurrent builds can count as a held controller press
        // and traverse multiple rows. A one-frame tap is the right fixture.
        await Frames(button is JoyButton.DpadUp or JoyButton.DpadDown or
            JoyButton.DpadLeft or JoyButton.DpadRight ? 1 : 3);
        Input.ParseInputEvent(new InputEventJoypadButton { Device = 0, ButtonIndex = button, Pressed = false });
        await Frames(3);
    }

    private async Task Keyboard(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        await Frames(3);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Frames(3);
    }

    private async Task Capture(string name)
    {
        await Frames(3);
        // Headless Godot uses a dummy renderer with no viewport texture.
        if (DisplayServer.GetName() == "headless") return;
        string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"),
            "..", "captures", "rac1-retail-ui"));
        System.IO.Directory.CreateDirectory(root);
        using var image = GetViewport().GetTexture().GetImage();
        var result = image.SavePng(System.IO.Path.Combine(root, name + ".png"));
        Require(result == Error.Ok, "Failed to save Godot menu capture");
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            yield return child;
            foreach (Node nested in Descendants(child)) yield return nested;
        }
    }
    private static void Require(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
}
