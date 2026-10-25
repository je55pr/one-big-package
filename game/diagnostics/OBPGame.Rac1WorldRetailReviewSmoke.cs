using OBP.Core;
using Godot;
using OBP.RAC1.Presentation;

namespace OneBigPackage;

public partial class OBPGame
{
    private Rac1PauseSmokeInputGuard? _retailReviewInputGuard;

    /// <summary>
    /// Final integration gate on live R&C1 scenes. Unlike the isolated GUI
    /// fixture, Start/Cross/Circle and focus travel through a paused world,
    /// while the real player and campaign inventory must remain untouched.
    /// No development teleports, forced unlocks or preview equipment.
    /// </summary>
    private async Task RunRac1WorldRetailNavigationSmokeAsync(string world)
    {
        if (_player is null || _world is not { Game.SourceGame: ObpSourceGame.Rac1 } ||
            !Rac1RetailUiAssets.IsLoaded)
            throw new InvalidOperationException($"{world} pause review needs a live R&C1 world and native UI assets.");
        // A --headless Godot root defaults to 64x64. Preserve the normal
        // 960x720 UI design viewport in *every* host, then restore the
        // caller's size when this fixture exits.
        var originalViewportSize = GetTree().Root.Size;
        GetTree().Root.Size = new Vector2I(960, 720);
        // InputMap actions are device-0 based in this test host. Keep
        // their real mapping and admit only exact injected event IDs.
        // Physical pad/mouse/keyboard events never reach the paused GUI.
        var physicalInputGuard = new Rac1PauseSmokeInputGuard();
        GetTree().Root.AddChild(physicalInputGuard);
        _retailReviewInputGuard = physicalInputGuard;
        Vector3 before = _player.GlobalPosition;
        int heldItem = _rac1Weapons.CurrentItemId;
        int[] slots = _rac1Weapons.QuickSelect.ToArray();
        int[] ammo = _rac1Weapons.Ammo.ToArray();
        var previousMouseMode = ApplicationPointer.MouseMode;
        try
        {
            if (!TryOpenPauseMenu() || !GetTree().Paused || _pauseMenu is not { RetailEnabled: true } menu)
                throw new InvalidOperationException($"{world} ordinary gameplay failed to open retail pause.");
            await ReviewFrames(10);
            var overlay = menu.GetNodeOrNull<Control>("NativeProjectedRootPanels");
            var root = overlay?.GetChildren().OfType<Rac1RetailMenuButton>().ToArray() ?? [];
            var visibleSize = GetViewport().GetVisibleRect().Size;
            if (root.Length != 6 || root.Any(button =>
                !button.Visible || button.GetGlobalRect().Size.X < visibleSize.X * .25f ||
                button.GetChildren().OfType<Rac1RetailPanelSurface>().Count() != 1) ||
                GetViewport().GuiGetFocusOwner() != root[0])
                throw new InvalidOperationException(
                    $"{world} root panels or opening focus invalid: viewport={visibleSize}, " +
                    $"panels=[{string.Join(", ", root.Select(button => button.GetGlobalRect().ToString()))}], " +
                    $"focus={GetViewport().GuiGetFocusOwner()?.Name}, mouse={ApplicationPointer.MouseMode}.");
            // UiPointerModality must show the cursor after *genuine*
            // mouse movement. The review fixture blocks external input,
            // but must not make that legitimate behaviour a failure.
            // Headless has no OS cursor and may always report Visible.
            // A rendered host may legitimately show it after fresh mouse
            // movement; check hidden mode only for controller modality.
            if (DisplayServer.GetName() != "headless" &&
                UiPointerModality.ControllerWasLastInput &&
                ApplicationPointer.MouseMode != Input.MouseModeEnum.Hidden)
                throw new InvalidOperationException(
                    $"{world} controller modality lost hidden pointer: viewport={visibleSize}, " +
                    $"mode={ApplicationPointer.MouseMode}, focus={GetViewport().GuiGetFocusOwner()?.Name}.");
            // Prove unsolicited action input is actually suppressed by
            // the guard, rather than merely relying on an idle test PC.
            using (var unsolicitedPress = new InputEventAction
            {
                Action = "ui_down",
                Pressed = true,
                Strength = 1f,
            })
            {
                Input.ParseInputEvent(unsolicitedPress); // Not admitted.
                await ReviewFrames(2);
            }
            using (var unsolicitedRelease = new InputEventAction
            {
                Action = "ui_down",
                Pressed = false,
            })
                Input.ParseInputEvent(unsolicitedRelease);
            await ReviewFrames(2);
            if (GetViewport().GuiGetFocusOwner() != root[0])
                throw new InvalidOperationException(
                    $"{world} unsolicited physical-equivalent input escaped the review guard.");
            if (DisplayServer.GetName() != "headless")
            {
                using var capture = GetViewport().GetTexture().GetImage();
                foreach (var button in root)
                {
                    var bounds = button.GetGlobalRect();
                    if (bounds.Position.X < 0 || bounds.Position.Y < 0 ||
                        bounds.End.X > capture.GetWidth() || bounds.End.Y > capture.GetHeight())
                        throw new InvalidOperationException($"{world} source panel extends beyond visible screen.");
                    var px = (Vector2I)(bounds.Position + bounds.Size * new Vector2(.10f, .48f));
                    var rgb = capture.GetPixel(px.X, px.Y);
                    if (rgb.R >= .26f || rgb.G >= .26f || rgb.B >= .30f)
                        throw new InvalidOperationException($"{world} source panel lost opaque backing: {rgb}");
                }
                string folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                    ProjectSettings.GlobalizePath("res://"), "..", "captures", $"rac1-ui-{world.ToLowerInvariant()}"));
                System.IO.Directory.CreateDirectory(folder);
                if (capture.SavePng(System.IO.Path.Combine(folder, "root-review.png")) != Error.Ok)
                    throw new InvalidOperationException($"{world} failed to capture live retail pause.");
            }
            await ReviewTap(JoyButton.DpadDown);
            if (GetViewport().GuiGetFocusOwner() != root[1])
                throw new InvalidOperationException(
                    $"{world} controller Down did not select Gadgets: " +
                    $"focus={GetViewport().GuiGetFocusOwner()?.Name}, " +
                    $"focusDown={root[0].FocusNeighborBottom}, root1={root[1].Name}, " +
                    $"view={GetViewport().GetVisibleRect().Size}, " +
                    $"uiDown={Input.IsActionPressed("ui_down")}.");
            await ReviewTap(JoyButton.DpadUp);
            if (GetViewport().GuiGetFocusOwner() != root[0])
                throw new InvalidOperationException($"{world} controller Up did not restore Weapons.");
            await ReviewTap(JoyButton.A);
            await ReviewPage(menu, Rac1RetailMenuPage.Weapons, world);
            await ReviewTap(JoyButton.B);
            await ReviewPage(menu, Rac1RetailMenuPage.Root, world);
            foreach (int _ in Enumerable.Range(0, 3))
                await ReviewTap(JoyButton.DpadDown);
            if (GetViewport().GuiGetFocusOwner() is not Rac1RetailMenuButton { Text: "Items" })
                throw new InvalidOperationException($"{world} root D-pad did not select Items.");
            await ReviewTap(JoyButton.A);
            await ReviewPage(menu, Rac1RetailMenuPage.Items, world);
            var items = EnumerateControls(menu).OfType<Rac1RetailEquipmentCell>()
                .Where(cell => cell.Name.ToString().StartsWith("NativeItemsCell_", StringComparison.Ordinal))
                .ToArray();
            if (items.Length != 9 || GetViewport().GuiGetFocusOwner() != items[0])
                throw new InvalidOperationException($"{world} native Items 3x3 or focus missing.");
            await ReviewTap(JoyButton.DpadRight);
            await ReviewTap(JoyButton.DpadDown);
            if (GetViewport().GuiGetFocusOwner() != items[4])
                throw new InvalidOperationException($"{world} Items controller moved to wrong grid cell.");
            await ReviewTap(JoyButton.A);
            await ReviewTap(JoyButton.B);
            await ReviewPage(menu, Rac1RetailMenuPage.Root, world);
            if (_player.GlobalPosition.DistanceTo(before) > .001f ||
                _rac1Weapons.CurrentItemId != heldItem ||
                !_rac1Weapons.QuickSelect.SequenceEqual(slots) ||
                !_rac1Weapons.Ammo.SequenceEqual(ammo))
                throw new InvalidOperationException($"{world} menu navigation mutated paused gameplay.");
            await ReviewTap(JoyButton.B); // Ordinary Circle from root resumes.
            await ReviewFrames(3);
            if (PauseMenuOpen || GetTree().Paused || ApplicationPointer.MouseMode != previousMouseMode)
                throw new InvalidOperationException($"{world} Circle did not restore gameplay/input mode.");
            if (!TryOpenPauseMenu() || _pauseMenu is not { RetailEnabled: true } reopened)
                throw new InvalidOperationException($"{world} pause failed to reopen after normal Circle exit.");
            await ReviewFrames(4);
            if (reopened.RetailPage != Rac1RetailMenuPage.Root ||
                GetViewport().GuiGetFocusOwner() is not Rac1RetailMenuButton { Text: "Weapons" })
                throw new InvalidOperationException($"{world} reopening retained stale page/focus.");
            GD.Print($"[rac1-ui-world-review] {world} PASS: projected panels, controller focus, Cross/Circle, Items, saved state, mouse mode, reopen");
        }
        finally
        {
            if (PauseMenuOpen)
                TryClosePauseMenu();
            // Restore the caller's viewport and input before resuming
            // ordinary world smoke; no fixture state may leak.
            _retailReviewInputGuard = null;
            GetTree().Root.RemoveChild(physicalInputGuard);
            physicalInputGuard.Free();
            GetTree().Root.Size = originalViewportSize;
            ApplicationPointer.MouseMode = previousMouseMode;
        }
        await ReviewFrames(3);
        // World physics legitimately resumes after Circle. Position is
        // asserted WHILE paused above, not after gravity/collisions resume.
        if (GetTree().Paused || PauseMenuOpen)
            throw new InvalidOperationException($"{world} world not restored after pause review.");
    }

    private async Task ReviewPage(PauseMenuUi menu, Rac1RetailMenuPage page, string world)
    {
        // Native transitions are 12 elapsed 60Hz ticks, NOT 12 or 25
        // render frames. Novalis can render faster than the transition
        // duration; fixed-frame waits falsely report an unresponsive page.
        for (int i = 0; i < 600 && menu.RetailPage != page; i++)
            await ReviewFrames(1);
        if (menu.RetailPage != page)
            throw new InvalidOperationException($"{world} page transition to {page} did not complete.");
        await ReviewFrames(3); // Allow Godot focus restoration after rebuild.
    }

    private async Task ReviewTap(JoyButton button)
    {
        // The separate UI smoke verifies raw joypad mappings. This
        // live-world fixture injects their resulting ui_* actions so the
        // viewport can exercise actual focus/press/back handling without
        // requiring a physical device or competing with attached hardware.
        string action = button switch
        {
            JoyButton.DpadUp => "ui_up",
            JoyButton.DpadDown => "ui_down",
            JoyButton.DpadLeft => "ui_left",
            JoyButton.DpadRight => "ui_right",
            JoyButton.A => "ui_accept",
            JoyButton.B => "ui_cancel",
            _ => throw new ArgumentOutOfRangeException(nameof(button)),
        };
        using var press = new InputEventAction { Action = action, Pressed = true, Strength = 1f };
        _retailReviewInputGuard?.Admit(press);
        Input.ParseInputEvent(press);
        await ReviewFrames(button is JoyButton.DpadUp or JoyButton.DpadDown or
            JoyButton.DpadLeft or JoyButton.DpadRight ? 1 : 3);
        using var release = new InputEventAction { Action = action, Pressed = false };
        _retailReviewInputGuard?.Admit(release);
        Input.ParseInputEvent(release);
        await ReviewFrames(3);
    }

    private async Task ReviewFrames(int count)
    {
        for (int i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static IEnumerable<Control> EnumerateControls(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Control control) yield return control;
            foreach (var nested in EnumerateControls(child)) yield return nested;
        }
    }
}
