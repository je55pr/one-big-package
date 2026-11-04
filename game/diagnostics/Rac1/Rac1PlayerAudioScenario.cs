using System.Numerics;
using OBP.RAC1.Gameplay;

namespace OBP.Game.Diagnostics;

public sealed class Rac1PlayerAudioScenario(DiagnosticScenarioDefinition definition, IDiagnosticPlayerAudioFixture fixture,
    IDiagnosticPlayerAudioEvents events, Action<string> log) : IDiagnosticScenario
{
    public DiagnosticScenarioDefinition Definition { get; } = definition;
    public async Task RunAsync(DiagnosticRunContext run)
    {
        var initial = fixture.Read();
        Require(initial.PlayerPresent && initial.Host.BankReady && initial.Host.LevelId == 0, "player audio smoke requires the Veldin player/bank");
        using var stage = fixture.BeginStage();
        await run.WaitAsync(DiagnosticFramePhase.Physics, 3);
        await Reset();
        await run.WaitAsync(DiagnosticFramePhase.Physics, 90);
        Require(events.Read().Length == 0, "idle emitted movement sounds");
        fixture.SetAnalogue(0, 1);
        await run.WaitAsync(DiagnosticFramePhase.Physics, 180);
        fixture.ClearInput();
        var steps = events.Read().Where(item => item.LevelSound).ToArray();
        Require(steps.Length is >= 4 and <= 32 && steps.Any(item => item.Sound == 2) && steps.Any(item => item.Sound == 4),
            $"run did not alternate the retail floor voices: {string.Join(',', steps.Select(item => item.Sound))}");
        Require(fixture.Read().Voices.All(voice => voice.Pcm), "player sounds did not reach the Godot PCM playback path");
        await run.WaitAsync(DiagnosticFramePhase.Physics, 90);
        int quiet = events.Read().Length;
        await run.WaitAsync(DiagnosticFramePhase.Physics, 90);
        Require(events.Read().Length == quiet, "stopped player kept emitting footsteps");
        await Reset(new(150, 0, 0));
        fixture.SetConditioned(0, .35);
        await run.WaitAsync(DiagnosticFramePhase.Physics, 180);
        fixture.ClearInput();
        var walk = fixture.Read(); var walkEvents = events.Read();
        Require(walk.SurfaceEffectMode == 1 && walkEvents.Any(item => item is { LevelSound: true, Sound: 6 or 8 }) &&
            !walkEvents.Any(item => item is { LevelSound: true, Sound: 2 or 4 }),
            $"floor sound category was dropped or the walk remained silent: mode={walk.SurfaceEffectMode}, cues=[{string.Join(';', walkEvents.Select(cue => $"{cue.Class}/{cue.Sound}/level={cue.LevelSound}/tick={cue.NativeTick}"))}]");
        log("[player-audio-smoke] idle/run/walk/stop and authored floor sound categories PASS");
        await Reset();
        fixture.SetAction(DiagnosticPlayerButton.Jump, true); await run.WaitAsync(DiagnosticFramePhase.Physics, 22);
        fixture.SetAction(DiagnosticPlayerButton.Jump, false); await run.WaitAsync(DiagnosticFramePhase.Physics, 150);
        Require(Heard(0, 26) && (Heard(0, 30) || Heard(0, 31)), "jump/landing animation sounds were missing");
        await Reset();
        fixture.SetAction(DiagnosticPlayerButton.Jump, true); await run.WaitAsync(DiagnosticFramePhase.Physics, 11);
        fixture.SetAction(DiagnosticPlayerButton.Jump, false); await run.WaitAsync(DiagnosticFramePhase.Physics, 4);
        fixture.SetAction(DiagnosticPlayerButton.Jump, true); await run.WaitAsync(DiagnosticFramePhase.Physics, 28);
        fixture.SetAction(DiagnosticPlayerButton.Jump, false);
        Require(Heard(0, 36), "aerial boost's sequence22 sound was missing");
        await Reset();
        fixture.SetAction(DiagnosticPlayerButton.Crouch, true); fixture.SetAction(DiagnosticPlayerButton.MoveRight, true); fixture.SetAction(DiagnosticPlayerButton.Jump, true);
        await run.WaitAsync(DiagnosticFramePhase.Physics, 2);
        Require(fixture.Read().TraversalAction == 11, "flip fixture did not enter native action11");
        await run.WaitAsync(DiagnosticFramePhase.Physics, 35); fixture.ClearInput();
        Require(Heard(0, 36), "directional flip animation sound was missing");
        log("[player-audio-smoke] jump/landing, aerial boost and directional flip cues PASS");
        foreach (int pack in new[] { 2, 3 })
        {
            await Reset(); SelectPack(pack);
            fixture.SetAction(DiagnosticPlayerButton.Jump, true); await run.WaitAsync(DiagnosticFramePhase.Physics, 15);
            fixture.SetAction(DiagnosticPlayerButton.Jump, false); await run.WaitAsync(DiagnosticFramePhase.Physics, 2);
            fixture.SetAction(DiagnosticPlayerButton.Jump, true);
            for (int tick = 0; tick < 70 && fixture.Read().TraversalAction != 8; tick++) await run.WaitAsync(DiagnosticFramePhase.Physics);
            Require(fixture.Read().TraversalAction == 8 && fixture.Read().Host.PackSound == (pack == 2 ? 2 : 18), "pack glide loop did not start");
            int count = events.Read().Count(item => item.Loop);
            await run.WaitAsync(DiagnosticFramePhase.Physics, 5);
            Require(events.Read().Count(item => item.Loop) == count, "pack loop restarted every physics update");
            Require(fixture.Back() && fixture.Read().TreePaused, "audio pause fixture did not pause");
            await run.WaitAsync(DiagnosticFramePhase.Process, 3);
            var voices = fixture.Read().Voices;
            Require(voices.Any(voice => voice.ForwardLoop && voice.Paused), "paused pack loop did not retain its paused playback");
            Require(voices.All(voice => !voice.Playing || voice.Paused), "paused player audio kept running");
            Require(fixture.Back() && !fixture.Read().TreePaused, "audio pause fixture did not resume");
            fixture.SetAction(DiagnosticPlayerButton.Jump, false);
            await Wait(() => fixture.Read().TraversalAction != 8, 50, "glide minimum hold / release");
            Require(fixture.Read().Host.PackSound is null, "released glide left its loop playing");
        }
        log("[player-audio-smoke] Heli/Thruster glide loop ownership and release PASS");
        await Reset(); fixture.SelectWeapon(Rac1WeaponId.Wrench);
        await TapWrench(); await WrenchTime(26); await TapWrench(); await WrenchTime(12); await TapWrench();
        await Wait(() => !fixture.Read().WrenchActive, 180, "audio combo recovery");
        Require(events.Read().Count(item => !item.Loop && item is { Class: 0, Sound: 27 }) == 3, "wrench combo did not sound exactly once for each swing");
        // Retail program 54 has four audible and four silent alternatives. Both are native outcomes; missing dispatch is a failure.
        Require(events.ReadOutcomes().Any(cue => !cue.Loop && !cue.LevelSound && cue.Class == 0 && cue.Sound == 54), "third swing's native voice program was not dispatched");
        await Reset(); fixture.StageRewardCrateInFront();
        int broken = fixture.Read().Host.DestroyedCrates;
        await TapWrench(); await Wait(() => fixture.Read().Host.DestroyedCrates == broken + 1, 120, "audible wrench crate contact");
        Require(events.Read().Any(item => item is { Class: 71, Sound: 0 or 1 }), "successful wrench contact was silent");
        await Reset(); fixture.SetAction(DiagnosticPlayerButton.Jump, true);
        await run.WaitAsync(DiagnosticFramePhase.Physics, 16); fixture.SetAction(DiagnosticPlayerButton.Jump, false);
        await TapWrench(); await Wait(() => !fixture.Read().WrenchActive, 180, "audible hyper-strike landing");
        Require(Heard(71, 2), "Hyper-Strike ground impact was silent");
        await Reset(); fixture.SetAction(DiagnosticPlayerButton.Crouch, true); await run.WaitAsync(DiagnosticFramePhase.Physics, 3);
        await TapWrench(); fixture.SetAction(DiagnosticPlayerButton.Crouch, false);
        await Wait(() => fixture.Read().Host.WrenchThrowActive, 100, "audio throw release");
        Require(fixture.Read().Host.ThrownWrenchSound == 14, "thrown wrench loop missing");
        await Wait(() => !fixture.Read().Host.WrenchThrowActive && !fixture.Read().WrenchActive, 240, "audio wrench catch");
        Require(fixture.Read().Host.ThrownWrenchSound is null && Heard(0, 5), "catch did not stop flight audio and play the catch cue");
        Require(events.Read().Count(item => item.Loop && item.Sound == 14) == 1, "wrench flight loop repeatedly restarted");
        log("[player-audio-smoke] timed combo whooshes/voice and throw-loop/catch PASS");
        await Reset(); fixture.ApplyContactHit(); fixture.ApplyContactHit(); await run.WaitAsync(DiagnosticFramePhase.Physics, 85);
        Require(fixture.Read().Host.Nanotech == 3 && events.Read().Count(item => item is { Class: 0, Sound: 39 }) == 1, "accepted hit voice was missing or a rejected hit repeated it");
        for (int i = 0; i < 3; i++)
        {
            if (i == 2)
            {
                fixture.SetEquipmentPreview(disable: false); fixture.SelectWeapon(Rac1WeaponId.Pyrocitor);
                fixture.SetAction(DiagnosticPlayerButton.Traversal, true); await run.WaitAsync(DiagnosticFramePhase.Physics, 5);
                Require(fixture.Read().Host.WeaponLoop, "fatal-hit fixture did not start its held weapon sound");
            }
            fixture.ApplyContactHit(); await run.WaitAsync(DiagnosticFramePhase.Physics, 85);
        }
        await run.WaitAsync(DiagnosticFramePhase.Physics, 50);
        Require(fixture.Read().Host.Dead && Heard(0, 60), "normal combat death voice was missing or stopped with movement");
        Require(fixture.Read().Host.PackSound is null && fixture.Read().Host.ThrownWrenchSound is null && !fixture.Read().Host.WeaponLoop, "death retained movement/weapon loops");
        log("[player-audio-smoke] accepted/protected enemy hits and normal combat death voices PASS");
        log("[player-audio-smoke] PASS");

        bool Heard(int cls, int sound) => events.Read().Any(item => !item.Loop && !item.LevelSound && item.Class == cls && item.Sound == sound);
        void SelectPack(int? pack)
        {
            for (int i = 0; i < 3 && fixture.Read().PreviewPack != pack; i++) fixture.CyclePack();
            Require(fixture.Read().PreviewPack == pack, "pack fixture could not select equipment");
        }
        async Task Reset(Vector3 offset = default)
        {
            fixture.ClearInput(); SelectPack(null); fixture.ResetPosition(offset);
            int supportedUpdates = 0;
            for (int frame = 0; frame < 180; frame++)
            {
                await run.WaitAsync(DiagnosticFramePhase.Physics);
                await run.WaitAsync(DiagnosticFramePhase.Process);
                var state = fixture.Read();
                supportedUpdates = state.Grounded && state.NativeGrounded ? supportedUpdates + 1 : 0;
                if (supportedUpdates >= 3) break;
            }
            Require(supportedUpdates >= 3, "Timed out waiting for player grounding.");
            await run.WaitAsync(DiagnosticFramePhase.Physics, 30); events.Clear();
        }
        async Task Wait(Func<bool> condition, int maximumFrames, string label)
        {
            for (int frame = 0; frame < maximumFrames; frame++)
            {
                if (condition()) { return; }
                await run.WaitAsync(DiagnosticFramePhase.Process);
            }
            throw new TimeoutException("Timed out waiting for RAC1 smoke phase: " + label + ".");
        }
        Task WrenchTime(float time) => Wait(() => fixture.Read() is { WrenchActive: true, WrenchBlending: false } state && state.WrenchAnimationTime >= time, 180, $"wrench authored time {time}");
        async Task TapWrench()
        {
            try { fixture.SetAction(DiagnosticPlayerButton.Wrench, true); await run.WaitAsync(DiagnosticFramePhase.Physics, 2); }
            finally { fixture.SetAction(DiagnosticPlayerButton.Wrench, false); }
            await run.WaitAsync(DiagnosticFramePhase.Physics, 2);
        }
    }
    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
}
