using Godot;
using OBP.Godot;

namespace OBP.Game;

/// <summary>Ordered application shutdown and audio retirement; borrows the existing session and operation owners.</summary>
internal static class GodotApplicationShutdown
{
    internal static async Task PrepareAsync(Node3D owner, UiModalCoordinator? modals,
        CaptureReplayCoordinator? capture, BugReportUiCoordinator? bugs, SourceVerificationCoordinator? verification,
        HostOperationScope operations, WorldSceneLifetime<GodotWorldResources>? worlds, Action releaseAudio)
    {
        // Stop gameplay before releasing its audio, otherwise a final physics
        // update can start another footstep/voice during shutdown.
        owner.ProcessMode = Node.ProcessModeEnum.Disabled;
        modals?.RetireAll();
        capture?.Recorder?.SuspendBugCaptureForQuit();
        bugs?.Dispose();
        Task verificationStopped = verification?.StopAsync() ?? Task.CompletedTask;
        Task hostStopped = operations.StopAsync();
        if (worlds is { } scenes)
        {
            scenes.CancelPreparation();
            await scenes.PendingPreparation;
        }
        await hostStopped;
        await verificationStopped;
        GodotCapturePresentation.ReportHostOperationFailures(owner, operations);
        if (capture?.Recorder is { } playtest)
            await operations.ObserveQuitWriteAsync("capture-flush", playtest.PrepareBugCaptureQuitAsync());
        GodotCapturePresentation.ReportHostOperationFailures(owner, operations);
        releaseAudio();

        // Godot 4.7.2 retires stopped playbacks on the mixing thread, then
        // releases their references in AudioServer's main-thread update. Tree
        // teardown alone cannot guarantee either has run. Observe two mixes
        // before the final quit; use wall time even with --fixed-fps.
        static double LastMixTime() => Time.GetTicksUsec() / 1_000_000d - AudioServer.GetTimeSinceLastMix();
        double lastMix = LastMixTime();
        ulong deadline = Time.GetTicksMsec() + 1000;
        int mixes = 0;
        while (mixes < 2 && Time.GetTicksMsec() < deadline)
        {
            await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.ProcessFrame);
            double mix = LastMixTime();
            if (mix > lastMix + .001d)
            {
                mixes++;
                lastMix = mix;
            }
        }
        await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print($"[lifecycle] audio retired before quit (mixes={mixes})");
    }


    internal static void Exit(Action releaseAudio, Func<GodotPlayerLifecycleHost?> player,
        Func<FusionWeaponHost?> weapons, PlayerSessionHost persistent, Action reportExit)
    {
        try { releaseAudio(); }
        finally
        {
            try { player()?.Dispose(); }
            finally
            {
                try { weapons()?.Dispose(); }
                finally
                {
                    persistent.RetireEquipment();
                    ApplicationLifecycle.SetQuitPreparation(null);
                    reportExit();
                }
            }
        }
    }
}
