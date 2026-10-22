using Godot;
using OBP.RAC1.Progression;

namespace OneBigPackage;

public partial class ControllerSmoke
{
    private async Task CheckMissionSceneAcknowledgementAsync()
    {
        // Synthetic presentation fixture, distinct from the human-played mission
        // gate. Real Cross edges must reach the production scene consumer while
        // the world is paused; the native session owns callback advancement.
        var records = new Dictionary<int, Rac1TalkRecord>
        {
            [0] = new(0, 0, 7, 1, 0, 0, 0, 0, 5),
            [1] = new(1, 0, 8, -1, 0, 0, 0, 0, 0),
        };
        var talk = new Rac1TalkSession(new(0, 834, 0), new(0, 1, records, new Dictionary<int, int>()), new());
        Require(talk.TryStart(new(System.Numerics.Vector3.Zero, 0, 4), System.Numerics.Vector3.Zero, 0, 3), "Scene fixture did not start");
        var previousFocus = Focus();
        var ui = new Rac1MissionSceneUi();
        ui.Completed += request =>
        {
            Require(talk.CompleteScene(request), "Scene consumer acknowledged a stale request");
            if (talk.PendingScene is { } next) ui.Present(next);
            else ui.QueueFree();
        };
        _game.AddChild(ui);
        bool paused = GetTree().Paused;
        try
        {
            GetTree().Paused = true;
            ui.Present(talk.PendingScene!);
            await Frames(3);
            await Capture("mission-scene");
            await Press(JoyButton.A);
            Require(talk.LastPlayed == 0 && talk.PendingScene?.NativeSceneId == 8,
                "Cross did not acknowledge exactly one scene and present its chained successor");
            await Press(JoyButton.A);
            Require(talk.LastPlayed == 1 && talk.PendingScene is null && talk.RecordIndex == -1,
                "Cross did not complete the chained scene");
        }
        finally
        {
            GetTree().Paused = paused;
            if (IsInstanceValid(ui) && !ui.IsQueuedForDeletion()) ui.QueueFree();
        }
        await Frames(3);
        if (IsInstanceValid(previousFocus)) previousFocus!.GrabFocus();
        GD.Print("[controller-smoke] paused mission scene consumer / Cross / chained callbacks PASS");
    }
}
