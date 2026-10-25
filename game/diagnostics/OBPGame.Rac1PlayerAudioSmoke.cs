using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;

namespace OBP.Game;

public partial class OBPGame
{
    private async Task RunRac1PlayerAudioSmokeAsync()
    {
        Node3D? fixture = null;
        try
        {
            Require(_player is not null && _rac1PlayerAudio is not null && _world?.LevelId == 0,
                "player audio smoke requires the Veldin player/bank");
            var player = _player!;
            Engine.MaxFps = 60;
            player.Scripted = false;
            Input.SetIgnoreJoypadOnUnfocusedApplication(false);
            // Keep the automated rendered run quiet without changing preferences.
            AudioServer.SetBusMute(0, true);
            fixture = new Node3D { Name = "PlayerAudioSyntheticFloor", Position = new(2000, 64, 2600) };
            AddChild(fixture);
            AddSyntheticTraversalMesh(fixture, "SoundCategory0", 11,
                (new(-80, 0, -80), new(80, 0, -80), new(80, 0, 80)),
                (new(-80, 0, -80), new(80, 0, 80), new(-80, 0, 80)));
            AddSyntheticTraversalMesh(fixture, "SoundCategory1", 43,
                (new(100, 0, -80), new(200, 0, -80), new(200, 0, 80)),
                (new(100, 0, -80), new(200, 0, 80), new(100, 0, 80)));
            await PhysicsFramesAsync(3);
            await ResetAsync();
            await PhysicsFramesAsync(90);
            Require(_rac1PlayerSoundTrace.Count == 0, "idle emitted movement sounds");
            SetAnalogueSmokeInput(0, 1);
            await PhysicsFramesAsync(180);
            ClearMovementSmokeInput();
            var steps = _rac1PlayerSoundTrace.Where(item => item.Cue.LevelSound).ToArray();
            Require(steps.Length is >= 4 and <= 32 && steps.Any(item => item.Cue.Sound == 2) && steps.Any(item => item.Cue.Sound == 4),
                $"run did not alternate the retail floor voices: {string.Join(',', steps.Select(item => item.Cue.Sound))}");
            Require(player.GetChildren().OfType<AudioStreamPlayer>().All(node => node.Stream is AudioStreamWav),
                "player sounds did not reach the Godot PCM playback path");
            await PhysicsFramesAsync(90);
            int quiet = _rac1PlayerSoundTrace.Count;
            await PhysicsFramesAsync(90);
            Require(_rac1PlayerSoundTrace.Count == quiet, "stopped player kept emitting footsteps");
            await ResetAsync(new(150, 0, 0));
            SetRac1SmokeConditionedInput(0, .35);
            await PhysicsFramesAsync(180);
            ClearMovementSmokeInput();
            Require(player.Rac1AudioSurfaceEffectMode == 1 &&
                _rac1PlayerSoundTrace.Any(item => item.Cue is { LevelSound: true, Sound: 6 or 8 }) &&
                !_rac1PlayerSoundTrace.Any(item => item.Cue is { LevelSound: true, Sound: 2 or 4 }),
                "floor sound category was dropped or the walk remained silent");
            GD.Print("[player-audio-smoke] idle/run/walk/stop and authored floor sound categories PASS");

            await ResetAsync();
            Input.ActionPress(RawGamepadInput.Jump);
            await PhysicsFramesAsync(22);
            Input.ActionRelease(RawGamepadInput.Jump);
            await PhysicsFramesAsync(150);
            Require(Heard(0, 26) && (Heard(0, 30) || Heard(0, 31)), "jump/landing animation sounds were missing");

            await ResetAsync();
            Input.ActionPress(RawGamepadInput.Jump);
            await PhysicsFramesAsync(11);
            Input.ActionRelease(RawGamepadInput.Jump);
            await PhysicsFramesAsync(4);
            Input.ActionPress(RawGamepadInput.Jump);
            await PhysicsFramesAsync(28);
            Input.ActionRelease(RawGamepadInput.Jump);
            Require(Heard(0, 36), "aerial boost's sequence22 sound was missing");

            await ResetAsync();
            Input.ActionPress(RawGamepadInput.Crouch);
            Input.ActionPress(RawGamepadInput.MoveRight);
            Input.ActionPress(RawGamepadInput.Jump);
            await PhysicsFramesAsync(2);
            Require(player.Rac1TraversalAction == 11, "flip fixture did not enter native action11");
            await PhysicsFramesAsync(35);
            ClearMovementSmokeInput();
            Require(Heard(0, 36), "directional flip animation sound was missing");
            GD.Print("[player-audio-smoke] jump/landing, aerial boost and directional flip cues PASS");

            foreach (int pack in new[] { 2, 3 })
            {
                await ResetAsync();
                SelectPack(pack);
                Input.ActionPress(RawGamepadInput.Jump);
                await PhysicsFramesAsync(15);
                Input.ActionRelease(RawGamepadInput.Jump);
                await PhysicsFramesAsync(2);
                Input.ActionPress(RawGamepadInput.Jump);
                for (int tick = 0; tick < 70 && player.Rac1TraversalAction != 8; tick++) await PhysicsFramesAsync(1);
                Require(player.Rac1TraversalAction == 8 && _rac1PackSound == (pack == 2 ? 2 : 18), "pack glide loop did not start");
                int count = _rac1PlayerSoundTrace.Count(item => item.Loop);
                await PhysicsFramesAsync(5);
                Require(_rac1PlayerSoundTrace.Count(item => item.Loop) == count, "pack loop restarted every physics update");
                Require(TryHandleInteractiveBack() && GetTree().Paused, "audio pause fixture did not pause");
                for (int tick = 0; tick < 3; tick++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var voices = player.GetChildren().OfType<AudioStreamPlayer>().Where(node => !node.IsQueuedForDeletion()).ToArray();
                Require(voices.Any(node => node.Stream is AudioStreamWav { LoopMode: AudioStreamWav.LoopModeEnum.Forward } && node.StreamPaused),
                    "paused pack loop did not retain its paused playback");
                Require(voices.All(node => !node.Playing || node.StreamPaused),
                    $"paused player audio kept running: {string.Join(';', voices.Select(node => $"{node.Name}: playing={node.Playing} paused={node.StreamPaused} process={node.CanProcess()}"))}");
                Require(TryHandleInteractiveBack() && !GetTree().Paused, "audio pause fixture did not resume");
                Input.ActionRelease(RawGamepadInput.Jump);
                await Rac1SmokeWaitAsync(() => player.Rac1TraversalAction != 8, 50, "glide minimum hold / release");
                Require(_rac1PackSound is null, "released glide left its loop playing");
            }
            GD.Print("[player-audio-smoke] Heli/Thruster glide loop ownership and release PASS");

            await ResetAsync();
            OnRac1WeaponSelectionRequested(Rac1WeaponId.Wrench);
            await TapWrenchSmokeAsync();
            await WaitWrenchTimeAsync(26);
            await TapWrenchSmokeAsync();
            await WaitWrenchTimeAsync(12);
            await TapWrenchSmokeAsync();
            await Rac1SmokeWaitAsync(() => !player.Rac1WrenchFrame.Active, 180, "audio combo recovery");
            Require(_rac1PlayerSoundTrace.Count(item => !item.Loop && item.Cue is { Class: 0, Sound: 27 }) == 3,
                "wrench combo did not sound exactly once for each swing");
            Require(Heard(0, 54), "third swing's native voice program was not played");

            await ResetAsync();
            var crate = _rac1CrateNodes.First(node => IsRac1GameplayActive(node) &&
                Rac1BoltCrate.ReadAuthored(node.Source)?.RewardCentre == 10);
            Vector3 forward = new(-(float)Math.Cos(player.Rac1CurrentYaw), 0, (float)Math.Sin(player.Rac1CurrentYaw));
            StageRac1CrateForSmoke(crate, player.GlobalPosition + forward * .8f);
            int broken = _rac1BoltCrates.DestroyedCrateCount;
            await TapWrenchSmokeAsync();
            await Rac1SmokeWaitAsync(() => _rac1BoltCrates.DestroyedCrateCount == broken + 1, 120, "audible wrench crate contact");
            Require(_rac1PlayerSoundTrace.Any(item => item.Cue is { Class: 71, Sound: 0 or 1 }), "successful wrench contact was silent");

            await ResetAsync();
            Input.ActionPress(RawGamepadInput.Jump);
            await PhysicsFramesAsync(16);
            Input.ActionRelease(RawGamepadInput.Jump);
            await TapWrenchSmokeAsync();
            await Rac1SmokeWaitAsync(() => !player.Rac1WrenchFrame.Active, 180, "audible hyper-strike landing");
            Require(Heard(71, 2), "Hyper-Strike ground impact was silent");

            await ResetAsync();
            Input.ActionPress(RawGamepadInput.Crouch);
            await PhysicsFramesAsync(3);
            await TapWrenchSmokeAsync();
            Input.ActionRelease(RawGamepadInput.Crouch);
            await Rac1SmokeWaitAsync(() => _rac1WrenchThrow.Active, 100, "audio throw release");
            Require(_rac1ThrownWrenchSound == 14, "thrown wrench loop missing");
            await Rac1SmokeWaitAsync(() => !_rac1WrenchThrow.Active && !player.Rac1WrenchFrame.Active, 240, "audio wrench catch");
            Require(_rac1ThrownWrenchSound is null && Heard(0, 5), "catch did not stop flight audio and play the catch cue");
            Require(_rac1PlayerSoundTrace.Count(item => item.Loop && item.Cue.Sound == 14) == 1, "wrench flight loop repeatedly restarted");
            GD.Print("[player-audio-smoke] timed combo whooshes/voice and throw-loop/catch PASS");

            await ResetAsync();
            var hit = new Rac1GameplayDamageEvent(Rac1GameplayEntityRef.Moby(new(749, 143)),
                Rac1GameplayEntityRef.Player, nativeDamage: 1, nativeMarker: Rac1Class749Hostile.AttackMarker);
            ApplyRac1EnemyPlayerDamage(hit);
            ApplyRac1EnemyPlayerDamage(hit); // The protected second hit must not restart a voice.
            await PhysicsFramesAsync(85);
            Require(_rac1Nanotech.Probe().Nanotech == 3 &&
                _rac1PlayerSoundTrace.Count(item => item.Cue is { Class: 0, Sound: 39 }) == 1,
                "accepted hit voice was missing or a rejected hit repeated it");
            for (int i = 0; i < 3; i++)
            {
                if (i == 2)
                {
                    SetRac1WeaponPreview(false);
                    OnRac1WeaponSelectionRequested(Rac1WeaponId.Pyrocitor);
                    Input.ActionPress(RawGamepadInput.Traversal);
                    await PhysicsFramesAsync(5);
                    Require(_rac1WeaponLoop is not null, "fatal-hit fixture did not start its held weapon sound");
                }
                ApplyRac1EnemyPlayerDamage(hit);
                await PhysicsFramesAsync(85);
            }
            await PhysicsFramesAsync(50);
            Require(_rac1Nanotech.Probe().IsDead && Heard(0, 60), "normal combat death voice was missing or stopped with movement");
            Require(_rac1PackSound is null && _rac1ThrownWrenchSound is null && _rac1WeaponLoop is null, "death retained movement/weapon loops");
            GD.Print("[player-audio-smoke] accepted/protected enemy hits and normal combat death voices PASS");
            GD.Print("[player-audio-smoke] PASS");
            ApplicationLifecycle.RequestQuit(this, "rac1-player-audio-smoke-pass", 0);

            bool Heard(int cls, int sound) => _rac1PlayerSoundTrace.Any(item => !item.Loop && !item.Cue.LevelSound && item.Cue.Class == cls && item.Cue.Sound == sound);
            void SelectPack(int? pack)
            {
                for (int i = 0; i < 3 && player.Rac1DevelopmentPackPreview != pack; i++) player.CycleRac1DevelopmentPack();
                Require(player.Rac1DevelopmentPackPreview == pack, "pack fixture could not select equipment");
            }
            async Task ResetAsync(Vector3 offset = default)
            {
                ClearMovementSmokeInput(); SelectPack(null);
                player.ApplyDevelopmentReset(fixture!.GlobalPosition + offset + Vector3.Up * .08f);
                await WaitForGroundedAsync(player, 180);
                await PhysicsFramesAsync(30);
                _rac1PlayerSoundTrace.Clear();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[player-audio-smoke] FAIL: {ex.Message}\n{ex.StackTrace}");
            ApplicationLifecycle.RequestQuit(this, "rac1-player-audio-smoke-fail", 1);
        }
        finally
        {
            ClearMovementSmokeInput();
            if (fixture is not null && IsInstanceValid(fixture)) fixture.QueueFree();
        }
    }
}
