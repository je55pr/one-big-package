using OBP.Core;
using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;

namespace OneBigPackage;

public partial class OBPGame
{
    private async Task RunRac1WrenchSmokeAsync()
    {
        Node3D? attachmentFixture = null;
        try
        {
            Require(_player is not null && _world is { Game.SourceGame: ObpSourceGame.Rac1, LevelId: 0 }, "wrench smoke needs Veldin");
            var player = _player!;
            Engine.MaxFps = 60;
            ClearMovementSmokeInput();
            await WaitForGroundedAsync(player, 360);
            Require(_rac1WrenchView is not null && _playerAvatarView is not null, "retail wrench/avatar did not load");
            OnRac1WeaponSelectionRequested(Rac1WeaponId.Wrench);
            await CaptureWrenchSmokeAsync("neutral-wrench");
            attachmentFixture = new Node3D
            {
                Name = "SyntheticWrenchRunIdleFloor",
                Position = new Vector3(2000, 64, 2600),
            };
            AddChild(attachmentFixture);
            AddSyntheticTraversalMesh(attachmentFixture, "WrenchRunIdleFloor", 11,
                (new(-20, 0, -20), new(20, 0, -20), new(20, 0, 20)),
                (new(-20, 0, -20), new(20, 0, 20), new(-20, 0, 20)));
            await PhysicsFramesAsync(3);
            player.ApplyDevelopmentReset(attachmentFixture.GlobalPosition + Vector3.Up * .08f);
            await WaitForGroundedAsync(player, 180);
            SetAnalogueSmokeInput(0, 1);
            await PhysicsFramesAsync(40);
            ClearMovementSmokeInput();
            bool sawStop = false, sawIdle = false;
            var handPositions = new HashSet<Vector3>();
            var attachmentController = (Rac1PlayerAnimationPresentationController)_playerAvatarAnimationController!;
            for (int tick = 0; tick < 120; tick++)
            {
                await PhysicsFramesAsync(1);
                var hand = attachmentController.CurrentAttachmentPose(Rac1WrenchAttachment.SocketKey);
                Require(hand is not null, "run-to-idle lost its hand attachment metadata");
                Vector3 expected = OBP.Godot.Player.PlayerAvatarView.ToGodotLocal(hand!.Origin,
                    _playerAvatar!.Axes, _playerAvatar.BaseHeight, true);
                Require(_rac1WrenchView!.HandLocal.DistanceTo(expected) < .00001f,
                    "rendered wrench froze instead of following the run-to-idle hand");
                sawStop |= attachmentController.OrdinaryStopSample is not null;
                if (CurrentPlayerAvatarSourceSequence() == 0)
                {
                    sawIdle = true;
                    handPositions.Add(_rac1WrenchView.HandLocal);
                }
            }
            Require(sawStop && sawIdle && handPositions.Count > 10,
                $"run-to-stop-to-idle did not keep a moving wrench attachment: stop={sawStop}, idle={sawIdle}, handPoses={handPositions.Count}");
            await CaptureWrenchSmokeAsync("run-to-idle-wrench");
            GD.Print("[wrench-smoke] neutral retracted model and run/stop/idle hand attachment PASS");
            player.DevelopmentControls.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            attachmentFixture.QueueFree();
            attachmentFixture = null;
            OnRac1WeaponSelectionRequested(Rac1WeaponId.FirstRanged);
            int ammo = _rac1Weapons.FirstRangedAmmo;
            await TapWrenchSmokeAsync();
            Require(player.Rac1WrenchFrame is { Active: true, ProfileId: 0 } &&
                CurrentPlayerAvatarSourceSequence() == 23 && _rac1WrenchView!.NativeSequence == 3,
                "Square with Bomb Glove equipped did not select first wrench body/model clips");
            Require(_rac1WrenchView!.Visible && _rac1Weapons.FirstRangedAmmo == ammo,
                "wrench was hidden or consumed Bomb Glove ammo");
            Require(!player.Rac1WrenchFrame.HitActive, "initial blend admitted an early hit");
            await CaptureWrenchSmokeAsync("first-swing");
            await WaitWrenchTimeAsync(26);
            await TapWrenchSmokeAsync();
            Require(player.Rac1WrenchFrame.ProfileId == 1 && CurrentPlayerAvatarSourceSequence() == 24 &&
                _rac1WrenchView.NativeSequence == 4, "second combo swing did not use native paired clips");
            await CaptureWrenchSmokeAsync("second-swing");
            await WaitWrenchTimeAsync(12);
            await TapWrenchSmokeAsync();
            Require(player.Rac1WrenchFrame.ProfileId == 2 && CurrentPlayerAvatarSourceSequence() == 25 &&
                _rac1WrenchView.NativeSequence == 5, "third combo swing did not use native paired clips");
            await CaptureWrenchSmokeAsync("third-swing");
            await Rac1SmokeWaitAsync(() => !player.Rac1WrenchFrame.Active, 180, "combo recovery");
            Require(_rac1Weapons.FirstRangedAmmo == ammo, "combo spent selected-weapon ammo");
            Require(_rac1WrenchView.Visible && !_rac1BombGloveView!.Visible,
                "wrench combo did not leave the last-used wrench in hand");
            await Rac1SmokeWaitAsync(() => CurrentPlayerAvatarSourceSequence() == 0, 60, "wrench standing recovery");
            var standing = (Rac1PlayerAnimationPresentationController)_playerAvatarAnimationController!;
            var previousPose = standing.Current.SampledPose;
            bool interpolated = false;
            for (int tick = 0; tick < 24; tick++)
            {
                await PhysicsFramesAsync(1);
                Require(CurrentPlayerAvatarSourceSequence() == 0 && standing.Current.SampledPose is not null &&
                    (standing.OrdinaryStandingSample is not null || standing.OrdinaryStopSample is { Sequence: 0 }),
                    "wrench recovery flashed locomotion or lost the native standing clock");
                var pose = standing.Current.SampledPose!;
                interpolated |= previousPose is not null && pose.FrameIndex == previousPose.FrameIndex &&
                    !pose.LocalPositions.AsSpan().SequenceEqual(previousPose.LocalPositions);
                previousPose = pose;
            }
            Require(interpolated, "post-wrench standing reverted to discrete low-rate keyframes");
            GD.Print("[wrench-smoke] combo 23/24/25 + own clips 3/4/5, held equipment/ammo, recovery PASS");

            Input.ActionPress(RawGamepadInput.Action);
            await PhysicsFramesAsync(2);
            long heldGeneration = _rac1WrenchHostGeneration;
            await PhysicsFramesAsync(120);
            Require(!player.Rac1WrenchFrame.Active && _rac1WrenchHostGeneration == heldGeneration,
                "holding Square automatically repeated the attack");
            Input.ActionRelease(RawGamepadInput.Action);
            await PhysicsFramesAsync(2);

            player.DevelopmentControls.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            Input.ActionPress(RawGamepadInput.Jump);
            await PhysicsFramesAsync(16);
            Input.ActionRelease(RawGamepadInput.Jump);
            await TapWrenchSmokeAsync();
            Require(player.Rac1WrenchFrame.NativeAction == 0x14 && CurrentPlayerAvatarSourceSequence() == 43 &&
                _rac1WrenchView.NativeSequence == 10, "airborne Square did not enter Hyper-Strike");
            Require(player.Rac1WrenchFrame.Damage == 2, "Hyper-Strike lost native damage two");
            await CaptureWrenchSmokeAsync("hyper-strike");
            bool dived = false;
            for (int tick = 0; tick < 180 && player.Rac1WrenchFrame.Active; tick++)
            {
                dived |= player.Velocity.Y < -8;
                await PhysicsFramesAsync(1);
            }
            Require(dived && !player.Rac1WrenchFrame.Active && player.IsOnFloor(), "Hyper-Strike failed dive/landing/recovery");
            GD.Print("[wrench-smoke] Hyper-Strike sequence43 + own clip10 rise/dive/landing PASS");

            player.DevelopmentControls.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            Input.ActionPress(RawGamepadInput.Crouch);
            await PhysicsFramesAsync(3);
            await TapWrenchSmokeAsync();
            Require(player.Rac1WrenchFrame.NativeAction == 0x15 && CurrentPlayerAvatarSourceSequence() == 26,
                "crouch/Square did not select Comet-Strike");
            Input.ActionRelease(RawGamepadInput.Crouch);
            await Rac1SmokeWaitAsync(() => _rac1WrenchThrow.Active, 100, "Comet-Strike release marker");
            Vector3 release = WrenchWorldPosition();
            Require(release.DistanceTo(player.GlobalPosition) > .3f, "throw spawned at feet instead of hand");
            await PhysicsFramesAsync(8);
            Require(_rac1WrenchThrow.Active && WrenchWorldPosition().DistanceTo(release) > 1,
                "thrown wrench did not travel outward");
            await CaptureWrenchSmokeAsync("comet-strike");
            await Rac1SmokeWaitAsync(() => !_rac1WrenchThrow.Active && !player.Rac1WrenchFrame.Active,
                240, "Comet-Strike return and loop recovery");
            Require(_rac1WrenchView.Visible && !_rac1BombGloveView!.Visible,
                "caught wrench did not stay in hand");
            GD.Print("[wrench-smoke] Comet-Strike release/flight/animated-hand catch/recovery PASS");

            // Contact fixtures are deliberately staged, unlike the separate
            // Veldin ordinary-play gate. All attacks still enter through input.
            OnRac1WeaponSelectionRequested(Rac1WeaponId.Wrench);
            player.DevelopmentControls.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            var crate = _rac1CrateNodes.First(node => IsRac1GameplayActive(node) &&
                Rac1BoltCrate.ReadAuthored(node.Source)?.RewardCentre == 10);
            Vector3 forward = new(-(float)Math.Cos(player.Rac1CurrentYaw), 0, (float)Math.Sin(player.Rac1CurrentYaw));
            StageRac1CrateForSmoke(crate, player.GlobalPosition + forward * .8f);
            int broken = _rac1BoltCrates.DestroyedCrateCount;
            await TapWrenchSmokeAsync();
            Require(_rac1BoltCrates.DestroyedCrateCount == broken, "crate broke during the entry blend");
            await Rac1SmokeWaitAsync(() => _rac1BoltCrates.DestroyedCrateCount == broken + 1, 120, "animated wrench/crate contact");
            await Rac1SmokeWaitAsync(() => !player.Rac1WrenchFrame.Active, 120, "contact recovery");
            GD.Print("[wrench-smoke] staged mesh-bound swept crate contact PASS");
            player.DevelopmentControls.ResetToSpawn();
            await WaitForGroundedAsync(player, 180);
            var thrownCrate = _rac1CrateNodes.First(node => IsRac1GameplayActive(node) &&
                Rac1BoltCrate.ReadAuthored(node.Source)?.RewardCentre == 10);
            forward = new(-(float)Math.Cos(player.Rac1CurrentYaw), 0, (float)Math.Sin(player.Rac1CurrentYaw));
            StageRac1CrateForSmoke(thrownCrate, player.GlobalPosition + forward * 4);
            int collected = _rac1BoltCrates.CollectedBolts;
            Vector3 throwOrigin = player.GlobalPosition;
            Input.ActionPress(RawGamepadInput.Crouch);
            await PhysicsFramesAsync(3);
            await TapWrenchSmokeAsync();
            Input.ActionRelease(RawGamepadInput.Crouch);
            await Rac1SmokeWaitAsync(() => _rac1BoltCrates.DestroyedCrateCount == broken + 2,
                160, "flying wrench/crate contact");
            await Rac1SmokeWaitAsync(() => _rac1BoltCrates.CollectedBolts > collected,
                90, "flying wrench bolt collection");
            Require(HorizontalDistance(throwOrigin, player.GlobalPosition) < .4f,
                "bolt collection depended on moving Ratchet to the distant crate");
            await Rac1SmokeWaitAsync(() => !player.Rac1WrenchFrame.Active, 240, "throw contact recovery");
            GD.Print("[wrench-smoke] staged throw/crate contact and distant bolt collection PASS");
            GD.Print("[wrench-smoke] PASS");
            ApplicationLifecycle.RequestQuit(this, "rac1-wrench-smoke-pass", 0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[wrench-smoke] FAIL: {ex.Message}\n{ex.StackTrace}");
            ApplicationLifecycle.RequestQuit(this, "rac1-wrench-smoke-fail", 1);
        }
        finally
        {
            if (attachmentFixture is not null && IsInstanceValid(attachmentFixture)) attachmentFixture.QueueFree();
            ClearMovementSmokeInput();
        }
    }

    private async Task TapWrenchSmokeAsync()
    {
        Input.ActionPress(RawGamepadInput.Action);
        await PhysicsFramesAsync(2);
        Input.ActionRelease(RawGamepadInput.Action);
        await PhysicsFramesAsync(2);
    }

    private Task WaitWrenchTimeAsync(float time) => Rac1SmokeWaitAsync(
        () => _player!.Rac1WrenchFrame.Active && !_player.Rac1WrenchAnimationBlending &&
            _player.Rac1TraversalAuthoredAnimationTime >= time, 180, $"wrench authored time {time}");

    private async Task CaptureWrenchSmokeAsync(string name)
    {
        string? directory = System.Environment.GetEnvironmentVariable("OBP_WRENCH_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var camera = _player!.Camera;
        Transform3D previous = camera.GlobalTransform;
        bool worldVisible = _sceneResult!.Root.Visible;
        bool uiVisible = _ui.Visible;
        Vector3 focus = _player.GlobalPosition + Vector3.Up * .8f;
        camera.GlobalPosition = focus + new Vector3(1.8f, .7f, 2.1f);
        camera.LookAt(focus);
        _sceneResult.Root.Visible = false;
        _ui.Visible = false;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, name + ".png"));
        _sceneResult.Root.Visible = worldVisible;
        _ui.Visible = uiVisible;
        camera.GlobalTransform = previous;
    }
}
