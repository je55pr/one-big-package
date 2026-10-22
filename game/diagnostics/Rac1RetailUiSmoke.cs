using Godot;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Presentation;

namespace OneBigPackage;

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
            var sample = new PauseMenuUi
            {
                RetailEnabled = true,
                MovementStatesAvailable = true,
                Weapons =
                [
                    new(10, "Bomb Glove", true, true, 0xEA6A),
                    new(15, "Blaster", false, false, 0xEA6F),
                ],
                Gadgets = [new(12, "Swingshot", true, false, 0xEA6C)],
                QuickSlots = [new(10, "Bomb Glove", true, true, 0xEA6A)],
                Items = [new(31, "Native item 31", true, false)],
            };
            int equips = 0, options = 0, resumed = 0;
            sample.EquipRequested += _ => equips++;
            sample.ControllerSettingsRequested += () => options++;
            sample.ResumeRequested += () => resumed++;
            AddChild(sample);
            await Frames(4);
            Require(GetViewport().GuiGetFocusOwner() is Button { Text: "WEAPONS" }, "No initial Weapons focus");
            await Capture("root");
            await Press(JoyButton.DpadDown);
            Require(GetViewport().GuiGetFocusOwner() is Button { Text: "GADGETS" },
                "D-pad navigation did not move from Weapons to Gadgets");
            await Press(JoyButton.DpadUp);
            Require(GetViewport().GuiGetFocusOwner() is Button { Text: "WEAPONS" },
                "D-pad Up did not restore Weapons");
            await Press(JoyButton.A);
            await Frames(25);
            Require(sample.RetailPage == Rac1RetailMenuPage.Weapons, $"Cross did not enter Weapons: page={sample.RetailPage}, focus={(GetViewport().GuiGetFocusOwner() as Button)?.Text}");
            var buttons = Descendants(sample).OfType<Button>().Where(b => b.Name.ToString().StartsWith("RetailItem_")).ToArray();
            Require(buttons.Length == 2 && buttons[1].Text.Contains("[LOCKED]"), "Ownership state missing");
            buttons[1].GrabFocus(); await Frames(2); await Press(JoyButton.A);
            Require(equips == 0, "Unowned weapon sent an equip request");
            buttons[0].GrabFocus(); await Frames(2); await Press(JoyButton.A);
            Require(equips == 1, "Owned weapon ignored Cross");
            await Capture("weapons");

            await Keyboard(Key.Escape); await Frames(25);
            Require(sample.RetailPage == Rac1RetailMenuPage.Root, "Keyboard Escape did not navigate back");
            await Open(sample, "GADGETS", Rac1RetailMenuPage.Gadgets);
            await Capture("gadgets");
            await Keyboard(Key.Escape); await Frames(25);
            await Open(sample, "QUICK SELECT", Rac1RetailMenuPage.QuickSelect);
            await Keyboard(Key.Escape); await Frames(25);
            await Open(sample, "ITEMS", Rac1RetailMenuPage.Items);
            await Keyboard(Key.Escape); await Frames(25);

            await Open(sample, "HELP", Rac1RetailMenuPage.Help);
            await FocusAndPress(sample, "Controls");
            await Frames(18);
            var help = Descendants(sample).OfType<Rac1HelpDeskView>().First();
            Require(help.State.Current == Rac1HelpDeskPresentation.Phase.Text, $"HelpDesk expansion not visible: {help.State.Current}");
            await Capture("helpdesk");
            await Keyboard(Key.Escape); await Frames(12);
            await Keyboard(Key.Escape); await Frames(25);
            Require(sample.RetailPage == Rac1RetailMenuPage.Root, "HelpDesk Back did not return to root");

            await Open(sample, "OPTIONS", Rac1RetailMenuPage.Options);
            await FocusAndPress(sample, "CONTROLLER SETTINGS");
            Require(options == 1, "Options did not forward settings event");
            await Capture("options");
            await Keyboard(Key.Escape); await Frames(25);
            await Open(sample, "OBP DEVELOPMENT TOOLS", Rac1RetailMenuPage.Development, contains: true);
            Require(Descendants(sample).OfType<CheckButton>().Any(c => c.Text == "SHOW DEBUG OUTPUT"),
                "Development controls disappeared");
            await Capture("development");
            await Keyboard(Key.Escape); await Frames(25);
            await FocusAndPress(sample, "RESUME");
            Require(resumed == 1, "Resume event did not fire");
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
        await Frames(3);
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
