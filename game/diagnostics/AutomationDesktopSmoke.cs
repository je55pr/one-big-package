using Godot;
using OBP.Godot;

namespace OBP.Game;

/// <summary>Asset-free desktop-isolation regression; run headlessly during development.</summary>
public partial class AutomationDesktopSmoke : Node
{
    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        try
        {
            await VerifyAsync(this);
            ApplicationLifecycle.RequestQuit(this, "automation-desktop-smoke-pass", 0);
        }
        catch (Exception error)
        {
            GD.PrintErr("[automation-desktop-smoke] FAIL: " + error);
            ApplicationLifecycle.RequestQuit(this, "automation-desktop-smoke-fail", 1);
        }
    }

    internal static async Task VerifyAsync(Node owner)
    {
        Require(ApplicationPointer.IsAutomated, "automated launch not recognized");
        foreach (var mode in Enum.GetValues<Input.MouseModeEnum>())
        {
            ApplicationPointer.MouseMode = mode;
            Require(ApplicationPointer.MouseMode == mode, "logical mode transition was lost");
            CheckDesktop(owner);
            await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.ProcessFrame);
            CheckDesktop(owner);
        }
        UiPointerModality.MenuOpened();
        Require(ApplicationPointer.MouseMode == Input.MouseModeEnum.Hidden, "controller menu modality not exercised");
        CheckDesktop(owner);
        UiPointerModality.MenuClosed(Input.MouseModeEnum.Captured);
        Require(ApplicationPointer.MouseMode == Input.MouseModeEnum.Captured, "captured-mode restore not exercised");
        CheckDesktop(owner);
        var player = new PlayerHost();
        owner.AddChild(player);
        await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.ProcessFrame);
        CheckDesktop(owner);
        player.QueueFree();
        GD.Print("[automation-desktop-smoke] PASS: logical capture, confinement, hiding, menu restore and player spawn leave the desktop cursor free");
    }

    private static void CheckDesktop(Node owner)
    {
        Require(Input.MouseMode == Input.MouseModeEnum.Visible, "real cursor was hidden, captured or confined");
        if (DisplayServer.GetName() != "headless")
            Require(owner.GetWindow().Unfocusable && owner.GetWindow().MousePassthrough, "test window can intercept desktop input");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
