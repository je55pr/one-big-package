using OBP.Core;
using Godot;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

/// <summary>Asset-free real GUI events over catalogue ports, with no desktop input.</summary>
public static class PauseLoadoutSmoke
{
    public static void VerifyOrdinarySurface(Node node)
    {
        if (node.IsQueuedForDeletion() || node is CanvasItem item && !item.IsVisibleInTree() || node is CanvasLayer layer && !layer.Visible || node is Node3D spatial && !spatial.IsVisibleInTree()) return;
        string text = node switch { Label label => label.Text, Button button => button.Text, RichTextLabel rich => rich.Text, Label3D sign => sign.Text, _ => "" };
        if (node is Control control) text += "\n" + control.TooltipText;
        Require(!System.Text.RegularExpressions.Regex.IsMatch(text, @"game sources|source (needed|ready|connected|unavailable)|attach|\.iso\b|rac[123]-ntsc|shared profile|provider|adapter|build id|native destination",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant), "Setup/provenance leaked into ordinary UI: " + text);
        foreach (var child in node.GetChildren()) VerifyOrdinarySurface(child);
    }

    public static async Task VerifyAsync(Node owner)
    {
        var definitions = PlayerEquipmentRegistrations.Catalog.Definitions.Concat(Enumerable.Range(0, 48).Select(index =>
            new EquipmentDefinition("fixture/weapon/" + index, "Future catalogue weapon " + index,
                index % 2 == 0 ? ObpSourceGame.Rac2 : ObpSourceGame.Rac3, "fixture", EquipmentCategory.Weapon,
                EquipmentImplementation.Implemented, new(index % 2 == 0 ? ObpSourceGame.Rac2 : ObpSourceGame.Rac3, 1000 + index)))).ToArray();
        SourceWeaponKey?[] slots = new SourceWeaponKey?[8];
        bool sourceAvailable = true, failEquip = false; int equips = 0, assignments = 0, resumes = 0, gadgets = 0;
        var actionErrors = new List<Exception>();
        var originalWindowSize = owner.GetWindow().Size;
        var menu = new PauseMenuUi
        {
            CatalogReader = () => definitions.Select(definition => new EquipmentCatalogEntry(definition,
                new(true, false, false, EquipmentResourceStatus.Available, EquipmentResourceStatus.Unavailable, 10, 20),
                sourceAvailable, definition.Implementation == EquipmentImplementation.Unimplemented ? EquipmentUsability.Unimplemented :
                    sourceAvailable ? EquipmentUsability.Ready : EquipmentUsability.MissingSource,
                definition.Implementation == EquipmentImplementation.Unimplemented ? "Native capability not recovered." :
                    sourceAvailable ? null : "Attach the source to use this equipment.")).ToArray(),
            SlotReader = () => slots,
            CatalogEquip = _ => { if (failEquip) throw new InvalidOperationException("Expected catalogue port failure."); equips++; return new(true); },
            CatalogErrorReporter = actionErrors.Add,
            CatalogUnequip = () => new(true),
            CatalogAssign = (slot, id) => { assignments++; slots[slot] = id is null ? null : definitions.Single(definition => definition.Id == id).Item; return new(true); },
            CatalogGadget = (_, _) => { gadgets++; return new(true); },
            SharedProfileStatusReader = () => "Shared profile saved; unavailable source contributors retained: rac2.",
        };
        bool wasPaused = owner.GetTree().Paused;
        try
        {
            menu.ResumeRequested += () => resumes++;
            owner.AddChild(menu); owner.GetTree().Paused = true;
            await Frames();
            VerifyOrdinarySurface(menu);
            Require(menu.UniversalEnabled && Buttons().Count(button => button.Name.ToString().StartsWith("ObpItem_")) == 6, "Catalogue did not paginate.");
            var card = Buttons().First(button => button.Name.ToString().StartsWith("ObpItem_"));
            card.GrabFocus(); await Pad(JoyButton.A);
            Require(Focus() is Button { Text: "EQUIP" }, "Inspect did not focus the primary action.");
            await Pad(JoyButton.A);
            Require(equips == 1 && owner.GetTree().Paused, "Controller equip doubled or resumed gameplay.");
            failEquip = true;
            Buttons().Single(button => button.Text == "EQUIP").GrabFocus(); await Pad(JoyButton.A);
            Require(actionErrors.Count == 1 && menu.FindChild("ObpCatalogStatus", true, false) is Label { Text: "The change could not be applied." },
                "Failed catalogue action was not reported.");
            failEquip = false;
            Buttons().Single(button => button.Text == "EQUIP").GrabFocus(); await Pad(JoyButton.A);
            Require(equips == 2, "Failed catalogue action permanently locked subsequent actions.");
            var next = Buttons().Single(button => button.Text == "NEXT");
            next.GrabFocus(); await Pad(JoyButton.A);
            Require(menu.UniversalPage == 1, "Controller could not reach the next catalogue page.");
            for (int page = 0; !next.Disabled && page < 20; page++)
            {
                next.GrabFocus(); await Frames(); // FollowFocus reveals paging inside the scroll area.
                await Click(next.GetGlobalRect().GetCenter());
            }
            Require(menu.UniversalPage >= 8 && Buttons().Any(button => button.Name.ToString().Contains("fixture_weapon_")), "Large future catalogue lost entries.");
            await Pad(JoyButton.RightShoulder);
            Require(menu.UniversalSection == "Gadgets", "Shoulder did not select gadget section.");
            var gadget = Buttons().First(button => button.Text.StartsWith("Heli-Pack", StringComparison.Ordinal));
            gadget.GrabFocus(); await Pad(JoyButton.A); await Pad(JoyButton.A);
            Require(gadgets == 1, "Gadget action did not reach the catalogue port once.");
            await Pad(JoyButton.RightShoulder);
            Require(menu.UniversalSection == "Appearance", "Shoulder did not select appearance section.");
            var unsupported = Buttons().Single(button => button.Name == "ObpItem_rac2_appearance_ratchet");
            await Click(unsupported.GetGlobalRect().GetCenter());
            Require(Buttons().Single(button => button.Text == "SELECT APPEARANCE").Disabled, "Unrecovered avatar became selectable.");
            await Pad(JoyButton.RightShoulder);
            Require(menu.UniversalSection == "Quick Select", "Shoulder did not select assignment section.");
            var lastSlot = Buttons().Single(button => button.Name == "ObpQuickSlot_7");
            lastSlot.GrabFocus(); await Pad(JoyButton.A);
            var assign = Buttons().Single(button => button.Text == "ASSIGN TO DIRECTION 8");
            assign.GrabFocus(); await Pad(JoyButton.A);
            Require(assignments == 1 && slots[7].HasValue, "Source-qualified eight-direction assignment failed.");
            Buttons().Single(button => button.Text == "CLEAR DIRECTION").GrabFocus(); await Pad(JoyButton.A);
            Require(assignments == 2 && slots[7] is null, "Clearing an assignment failed.");
            sourceAvailable = false;
            menu.ShowUniversalSection("Weapons"); await Frames();
            Require(Buttons().Single(button => button.Text == "EQUIP").Disabled &&
                menu.FindChild("ObpCatalogDetails", true, false) is Label { Text: var detail } && detail.Contains("temporarily unavailable"),
                "Lost source did not disable equip with a reason.");
            owner.GetWindow().Size = new(640, 360); await Frames(5);
            var scroll = (ScrollContainer)menu.FindChild("ObpCatalogScroll", true, false);
            Require(scroll.GetHScrollBar().MaxValue <= scroll.Size.X + 1, "Catalogue overflowed horizontally at 640px.");
            Require(scroll.GetGlobalRect().End.Y <= owner.GetViewport().GetVisibleRect().End.Y, "Short window pushed catalogue actions below the viewport.");
            menu.RestoreFocus(); Require(Focus() is not null && menu.IsAncestorOf(Focus()), "Pause lost controller focus after resize.");
            await Pad(JoyButton.Start); Require(resumes == 1, "Start did not resume once.");
            await Pad(JoyButton.B); Require(resumes == 2, "Back did not resume once.");
            GD.Print("[pause-loadout-smoke] PASS: large mixed catalogue, mouse/gamepad, paging, actions, source loss, unsupported avatars, resize and pause isolation");
        }
        finally { owner.GetTree().Paused = wasPaused; menu.QueueFree(); owner.GetWindow().Size = originalWindowSize; await Frames(); }

        IEnumerable<Button> Buttons() => menu.FindChildren("*", "Button", true, false).OfType<Button>();
        Control? Focus() => owner.GetViewport().GuiGetFocusOwner();
        async Task Frames(int count = 3) { for (int index = 0; index < count; index++) await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Pad(JoyButton button)
        {
            Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = button, Pressed = true }); await Frames();
            Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = button, Pressed = false }); await Frames();
        }
        async Task Click(Vector2 position)
        {
            Input.ParseInputEvent(new InputEventMouseMotion { Position = position });
            Input.ParseInputEvent(new InputEventMouseButton { Position = position, ButtonIndex = MouseButton.Left, Pressed = true }); await Frames();
            Input.ParseInputEvent(new InputEventMouseButton { Position = position, ButtonIndex = MouseButton.Left, Pressed = false }); await Frames();
        }
    }

    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
}
