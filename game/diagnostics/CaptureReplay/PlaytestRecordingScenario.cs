namespace OBP.Game.Diagnostics;

/// <summary>Ordinary device-zero input; the recorder remains responsible for committing a completed tape and quitting.</summary>
public sealed class PlaytestRecordingScenario(CaptureDiagnosticContext context, ICaptureRecorderDiagnosticFixture recorder) : CaptureScenarioSupport(context)
{
    public async Task RunAsync()
    {
        for (int i = 0; i < 360 && recorder.Read().WaitingForEntry; i++) await Frames();
        Require(recorder.Read() is { RecorderPresent: true, PlayerPresent: true, WaitingForEntry: false }, "Playtest recorder/player missing or entry did not finish.");
        await MovementAndEquipmentAsync();
        await WheelAndPauseAsync();
        await MarkAndFinishAsync();
    }
    private async Task MovementAndEquipmentAsync()
    {
        await Physics(30); Axis(CaptureAxis.LeftY, -.55f); await Physics(40); Axis(CaptureAxis.LeftY, 0);
        await Physics(15); Key(CaptureKey.Space, true); await Physics(10); Key(CaptureKey.Space, false);
        await Physics(75); Axis(CaptureAxis.RightX, .65f); await Physics(35); Axis(CaptureAxis.RightX, 0);
        Key(CaptureKey.E, true); await Physics(3); Key(CaptureKey.E, false); await Physics(25);
        Key(CaptureKey.E, true); await Physics(3); Key(CaptureKey.E, false); await Physics(150);
        Pad(CaptureButton.X, true); await Physics(3); Pad(CaptureButton.X, false); await Physics(75);
    }
    private async Task WheelAndPauseAsync()
    {
        Pad(CaptureButton.Y, true); await Physics(20);
        Require(recorder.Read().QuickSelectOpen, "Recorded Triangle did not open Quick Select.");
        Pad(CaptureButton.Y, false); await Physics(5);
        Require(!recorder.Read().QuickSelectOpen, "Recorded Triangle release did not close Quick Select.");
        recorder.ResizeWindow(1000, 700); await Physics(10);
        Pad(CaptureButton.Start, true); await Frames(); Pad(CaptureButton.Start, false); await Frames(15);
        Require(recorder.Read().PauseOpen, "Recorded Start did not pause.");
        Pad(CaptureButton.Down, true); await Frames(2); Pad(CaptureButton.Down, false); await Frames(5);
    }
    private async Task MarkAndFinishAsync()
    {
        Key(CaptureKey.F8, true, CaptureKeyOrigin.Logical); await Frames();
        Key(CaptureKey.F8, false, CaptureKeyOrigin.Logical); await Frames();
        Key(CaptureKey.F8, true, CaptureKeyOrigin.Physical); await Frames();
        Key(CaptureKey.F8, false, CaptureKeyOrigin.Physical); await Frames();
        Pad(CaptureButton.X, true); await Frames(); Pad(CaptureButton.X, false);
        if (recorder.Read().LevelId == 0) Key(CaptureKey.F9, true, CaptureKeyOrigin.Physical);
        else Pad(CaptureButton.Back, true);
    }
}
