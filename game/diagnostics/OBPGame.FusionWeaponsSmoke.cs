using OBP.Core;
using System.Text.Json;
using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    /// <summary>Ordinary starting point and input only; no world or inventory staging.</summary>
    private async Task RunFusionRecordingSmokeAsync()
    {
        try
        {
            for (int i = 0; i < 360 && _playtest?.WaitingForEntry == true; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_fusion is not null && _fusionAssets?.Problems.Count == 0, "fusion recording requires all three original sources");
            await WaitForGroundedAsync(_player!, 180);
            await PhysicsFramesAsync(20);
            int openingAmmo = FusionTestActive ? 10 : 20;
            Require(_rac1Weapons.FirstRangedAmmo == openingAmmo, "fresh Bomb Glove ammo");
            await CaptureFusionAsync("ordinary-opening");
            await SelectFusionByInputAsync(0);
            await FusionFireInputAsync(16); // The ordinary entry still draws the glove through the native Circle handoff.
            Require(_rac1Weapons.FirstRangedAmmo == openingAmmo - 1, "recorded Bomb Glove did not consume one round");
            await PhysicsFramesAsync(90);
            await FusionMarkAsync();
            await SelectFusionByInputAsync(1);
            await PhysicsFramesAsync(30);
            await CaptureFusionAsync("lancer-idle");
            await FusionFireInputAsync(60);
            Require(_fusion!.LancerSession.Ammo < 40, "recorded Lancer did not fire");
            await CaptureFusionAsync("ordinary-lancer");
            await FusionCheckWrenchPersistenceAsync();
            await PhysicsFramesAsync(30);
            await FusionMarkAsync();
            await SelectFusionByInputAsync(2);
            await PhysicsFramesAsync(30);
            await CaptureFusionAsync("cannon-idle");
            await FusionFireInputAsync(90);
            Require(_fusion.CannonSession.Ammo <= 9, "recorded Shock Cannon did not spread and charge");
            await CaptureFusionAsync("ordinary-cannon");
            await FusionCheckWrenchPersistenceAsync();
            await PhysicsFramesAsync(45);
            await FusionMarkAsync();
            Vector3 start = _player!.GlobalPosition;
            Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = -1 });
            await PhysicsFramesAsync(20);
            Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = 0 });
            await PhysicsFramesAsync(15);
            Require(_player.GlobalPosition.DistanceTo(start) > .2f, $"movement did not recover after equipment selection: {start}->{_player.GlobalPosition}; phase={_player.Rac1MovementPhase}");
            GD.Print($"[fusion-record] PASS ordinary opening/input, three weapons, native ammo, charge and movement; foreign hits={_fusionAcceptedHits}");
            FusionKey(Key.F9, true);
        }
        catch (Exception error)
        {
            GD.PrintErr("[fusion-record] FAIL: " + error);
            ApplicationLifecycle.RequestQuit(this, "fusion-record-fail", 2);
        }
    }

    /// <summary>
    /// Staged collision fixtures using original authored target objects. Kept
    /// separate from the ordinary input recording above; not a retail replay.
    /// </summary>
    private async Task RunFusionWeaponsSmokeAsync()
    {
        Node3D? fixture = null;
        try
        {
            Engine.MaxFps = 60;
            long preparationFrame = _frame;
            bool wasPreparing = _fusionPreparation is not null;
            await WaitForSharedWeaponsSmokeAsync();
            Require(!wasPreparing || _frame > preparationFrame, "source preparation blocked application frames");
            Require(_fusion is not null && _fusionAssets?.Problems.Count == 0 && _player is not null, "fusion smoke requires three verified sources on Veldin");
            ClearMovementSmokeInput();
            GD.Print($"[fusion-smoke] shared source preparation yielded {_frame - preparationFrame} application frames");
            string normalInventory = JsonSerializer.Serialize(_fusionOriginalCampaign!.Weapons.CapturePersistentState());
            await WaitForGroundedAsync(_player!, 180);
            await SelectFusionByInputAsync(1);
            await PhysicsFramesAsync(30);
            RequireFusionGunAlignment();
            await CaptureFusionProfileAsync("lancer-profile");
            Require(_fusionHeldViews[FusionWeaponSession.Lancer].GetChildren().OfType<MeshInstance3D>().Any(), "original Lancer mesh absent");
            Require(_rac1WrenchView is { Visible: false }, "Lancer retained the R&C1 wrench model");
            int beforeWrench = _fusion!.LancerSession.Ammo;
            FusionKey(Key.X, true);
            await Rac1SmokeWaitAsync(() => _player!.Rac1WrenchFrame.Active, 20, "Square wrench input");
            Require(_rac1WrenchView!.Visible && !_fusionHeldViews[FusionWeaponSession.Lancer].Visible, "Square did not own the hand model");
            FusionKey(Key.X, false);
            await Rac1SmokeWaitAsync(() => !_player!.Rac1WrenchFrame.Active, 120, "Square recovery");
            await PhysicsFramesAsync(2);
            Require(_fusion.LancerSession.Ammo == beforeWrench && _rac1WrenchView.Visible &&
                !_fusionHeldViews[FusionWeaponSession.Lancer].Visible && _rac1HeldItem.Hand.WrenchHeld,
                "Square did not leave the wrench equipped after recovery");
            await PhysicsFramesAsync(30);
            Require(_rac1WrenchView.Visible && !_fusionHeldViews[FusionWeaponSession.Lancer].Visible,
                "selected foreign equipment reclaimed the idle hand");
            await FusionFireInputAsync(1); // Circle explicitly draws the selected equipment again.
            Require(_fusionHeldViews[FusionWeaponSession.Lancer].Visible && !_rac1WrenchView.Visible,
                $"Circle did not return hand ownership to selected equipment: hand={_fusion.Hand.Equipment}, " +
                $"wrench={_player!.Rac1WrenchFrame.Active}, hanging={_player.Rac1Hanging}, tether={_player.Rac1SwingshotActive}, alive={PlayerAlive}");
            await SelectFusionByInputAsync(2);
            await PhysicsFramesAsync(30);
            RequireFusionGunAlignment();
            await CaptureFusionProfileAsync("cannon-profile");
            Require(_fusionHeldViews[FusionWeaponSession.ShockCannon].GetChildren().OfType<MeshInstance3D>().Any(), "original Cannon mesh absent");
            await SelectFusionByInputAsync(0);
            await CaptureFusionAsync("ordinary-selector");
            GD.Print("[fusion-smoke] three source-qualified entries, original held assets and ordinary selector input PASS");
            var attachedAssets = _fusionAssets!;
            using (var missingAssets = new FusionWeaponAssets(new FusionWeaponData([])))
            {
                _fusionAssets = missingAssets;
                try
                {
                    Require(missingAssets.Problems.Count == 3, "missing sources were silently admitted");
                    await SelectFusionByInputAsync(1);
                    int unavailableAmmo = _fusion!.LancerSession.Ammo;
                    await FusionFireInputAsync(20);
                    Require(_fusion.LancerSession.Ammo == unavailableAmmo && !_fusionHeldViews[FusionWeaponSession.Lancer].Visible &&
                        _hudState.Current.CurrentWeapon?.Status?.Contains("Game Sources", StringComparison.Ordinal) == true,
                        "missing source fired, displayed a model or lacked actionable status");
                    await CaptureFusionAsync("missing-source");
                }
                finally { _fusionAssets = attachedAssets; }
            }
            await SelectFusionByInputAsync(0);

            fixture = new Node3D { Name = "FusionStagedContactFixture", Position = new(-2000, 64, 2600) }; AddChild(fixture);
            AddSyntheticTraversalMesh(fixture, "FusionFloor", 11,
                (new(-30, 0, -30), new(30, 0, -30), new(30, 0, 30)),
                (new(-30, 0, -30), new(30, 0, 30), new(-30, 0, 30)));
            await PhysicsFramesAsync(3);
            _player!.ApplyDevelopmentReset(fixture.GlobalPosition + new Vector3(6, .05f, 0));
            await WaitForGroundedAsync(_player, 120);
            Rac1SmokePlaceGroundedFacing(_player.GlobalPosition, Vector3.Left);
            await PhysicsFramesAsync(10);
            foreach (int selection in new[] { 1, 2 })
            {
                _player.ApplyDevelopmentReset(fixture.GlobalPosition + Vector3.Up * .05f);
                await WaitForGroundedAsync(_player, 120);
                await SelectFusionByInputAsync(selection);
                foreach (bool movingJump in new[] { false, true }) await CheckFusionJumpAttachmentAsync(movingJump);
                await CheckFusionMovementTransitionsAsync();
                await FusionCheckWrenchPersistenceAsync();
            }
            await SelectFusionByInputAsync(0);
            _player.ApplyDevelopmentReset(fixture.GlobalPosition + new Vector3(6, .05f, 0));
            await WaitForGroundedAsync(_player, 120);
            Rac1SmokePlaceGroundedFacing(_player.GlobalPosition, Vector3.Left);
            await PhysicsFramesAsync(10);
            var crates = _rac1CrateNodes.Where(n => n.Source.NativeClassId == 500 && IsRac1GameplayActive(n)).Take(5).ToArray();
            Require(crates.Length == 5, "five authored crate objects required");
            await FusionFireInputAsync(3);
            await Rac1SmokeWaitAsync(() => _rac1Projectiles.Values.Any(p => !p.Motion.Flying), 180, "Bomb Glove floor impact");
            Vector3 landing = FromEffectNative(_rac1Projectiles.Values.First(p => !p.Motion.Flying).Motion.Position);
            await PhysicsFramesAsync(120);
            StageRac1CrateForSmoke(crates[0], new(landing.X, fixture.GlobalPosition.Y, landing.Z));
            await PhysicsFramesAsync(3);
            await FusionFireInputAsync(3);
            await Rac1SmokeWaitAsync(() => !IsRac1GameplayActive(crates[0]), 180, "Bomb Glove authored crate reaction");
            Require(_rac1Weapons.FirstRangedAmmo == 8, "Bomb Glove staged ammo");
            await PhysicsFramesAsync(120);

            await SelectFusionByInputAsync(1);
            StageRac1CrateForSmoke(crates[1], _player.GlobalPosition + EquipmentForward * 4);
            await PhysicsFramesAsync(3);
            await FusionFireInputAsync(30);
            Require(!IsRac1GameplayActive(crates[1]) && _fusionLastDamage is { SourceDamage: .5f } &&
                _fusionLastDamage.Shot.Weapon == FusionWeaponSession.Lancer, "Lancer half-damage crate adapter");
            await CaptureFusionAsync("lancer-contact");
            await PhysicsFramesAsync(30); // Drain shots already past the broken crate before placing the robot.

            var robot = _rac1Enemies.Values.First(e => e.Node.Source.NativeClassId == 1440 && !e.RewardEmitted);
            PlaceWeaponSmokeEnemy(robot, _player.GlobalPosition + EquipmentForward * 5);
            await PhysicsFramesAsync(7);
            float health = _rac1Class1440!.Get(robot.Node.Source.InstanceIndex).Health;
            await FusionFireInputAsync(25);
            float after = _rac1Class1440.Get(robot.Node.Source.InstanceIndex).Health;
            Require(after == health - .5f && !robot.RewardEmitted, $"Lancer fractional robot reaction/cooldown: {health}->{after}");
            GD.Print("[fusion-smoke] Bomb Glove / Lancer original ammo and authored crate/robot consequences PASS");

            await SelectFusionByInputAsync(2);
            StageRac1CrateForSmoke(crates[2], _player.GlobalPosition + EquipmentForward * 3);
            await PhysicsFramesAsync(3);
            await FusionFireInputAsync(10);
            Require(!IsRac1GameplayActive(crates[2]) && _fusionLastDamage?.Shot.Weapon == FusionWeaponSession.ShockCannon, "Shock spread crate reaction");
            await PhysicsFramesAsync(30);
            FusionKey(Key.E, true);
            await PhysicsFramesAsync(15);
            StageRac1CrateForSmoke(crates[3], _player.GlobalPosition + EquipmentForward * 4);
            await Rac1SmokeWaitAsync(() => _fusion!.CannonFrame?.BeamUpdatesRemaining > 0, 90, "charged beam");
            Require(!IsRac1GameplayActive(crates[3]) && _fusionLastDamage is { SourceDamage: 50, Kind: "beam" }, "charged raw-damage crate reaction");
            Require(_fusionBeam is { Visible: true } && _fusionAudioStarted > 4, "source beam/audio feedback");
            await CaptureFusionAsync("charged-beam");
            FusionKey(Key.E, false);
            await PhysicsFramesAsync(45);

            // A wall must consume a projectile before a later eligible target.
            await SelectFusionByInputAsync(1);
            var wall = new StaticBody3D { Name = "FusionOcclusionWall", Position = _player.GlobalPosition + EquipmentForward * 2 + Vector3.Up * 2 };
            wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(.15f, 6, 8) } }); fixture.AddChild(wall); wall.GlobalPosition = _player.GlobalPosition + EquipmentForward * 2 + Vector3.Up * 2;
            StageRac1CrateForSmoke(crates[4], _player.GlobalPosition + EquipmentForward * 5);
            await PhysicsFramesAsync(3);
            await FusionFireInputAsync(25);
            Require(IsRac1GameplayActive(crates[4]), "Lancer hit an authored crate through blocking world geometry");
            wall.QueueFree();
            ClearFusionTransients();
            await PhysicsFramesAsync(3);
            Require(_fusionProjectiles.Count == 0 && _fusion!.LancerSession.ActiveShots.Count == 0 && _fusionEffects.Count == 0 &&
                _fusionPendingAudio.Count == 0 && _fusionAudioPlayers.Count == 0 && _rac1Projectiles.Count == 0,
                "fusion reset retained projectiles/effects/audio");
            Require(JsonSerializer.Serialize(_fusionOriginalCampaign.Weapons.CapturePersistentState()) == normalInventory, "fusion contaminated campaign inventory");
            _args = _args with { FusionTest = false };
            Require(OnDestinationChosen(_activeDestination!), "normal Veldin re-entry rejected");
            await PhysicsFramesAsync(5);
            Require(_fusion is not null && !FusionTestActive && ReferenceEquals(_fusionAssets, attachedAssets) &&
                _fusionPreparation is null && _fusionHeldViews.Count == 2,
                "world re-entry discarded shared assets or retained isolated session ownership");
            Require(JsonSerializer.Serialize(_rac1Weapons.CapturePersistentState()) == normalInventory, "normal inventory was not restored");
            Require(ReferenceEquals(_fusion!.Rac1Inventory, _rac1CampaignSession.Weapons), "default weapons replaced the campaign inventory");
            FusionKey(Key.Q, true);
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_quickSelect!.Entries.Any(e => e.Weapon == FusionWeaponSession.Lancer) &&
                _quickSelect.Entries.Any(e => e.Weapon == FusionWeaponSession.ShockCannon), "default Quick Select omitted the two foreign weapons");
            FusionKey(Key.Q, false);
            await PhysicsFramesAsync(2);
            await SelectFusionByInputAsync(1);
            Require(_fusionPreparation is null && ReferenceEquals(_fusionAssets, attachedAssets), "selection restarted source processing");
            Require(!FusionTestActive && FusionConfiguration!.LoadoutId == FusionWeaponSession.DefaultLoadoutId,
                "ordinary selection entered the isolated test loadout");
            await PhysicsFramesAsync(3);
            Require(_fusionHeldViews[FusionWeaponSession.Lancer].GetChildren().OfType<MeshInstance3D>().Any(),
                "background preparation did not attach the original Lancer model");
            GD.Print("[fusion-smoke] default first equip reuses prepared shared models, textures and audio PASS");
            await FusionFireInputAsync(20);
            Require(_fusion.LancerSession.Ammo < 40, "default Lancer did not fire");
            await SelectFusionByInputAsync(2);
            await FusionFireInputAsync(12);
            Require(_fusion.CannonSession.Ammo < 20, "default Cannon did not fire");
            await SelectFusionByInputAsync(0);
            Require(JsonSerializer.Serialize(_rac1Weapons.CapturePersistentState()) == normalInventory &&
                ReferenceEquals(_rac1Weapons, _rac1CampaignSession.Weapons), "default weapons replaced or modified native inventory");
            GD.Print("[fusion-smoke] default Quick Select, both foreign weapons and normal native inventory PASS");
            ShowDestinationSelector();
            Require(ReferenceEquals(_fusionAssets, attachedAssets), "world exit discarded shared equipment");
            var originalSources = _sources;
            try
            {
                _sources = new(TrilogySourceDefinitions.All);
                PrepareSharedWeaponSources(force: true);
                await WaitForSharedWeaponsSmokeAsync();
                Require(_fusionAssets?.Problems.Count == 3 && !ReferenceEquals(_fusionAssets, attachedAssets),
                    "source change retained stale verified assets");
                _sources = originalSources;
                PrepareSharedWeaponSources(force: true);
                var canceledPreparation = _fusionPreparation!;
                ResetFusionWeapons();
                Require(ReferenceEquals(_fusionPreparation, canceledPreparation), "level reset canceled shared preparation");
                _sources = new(TrilogySourceDefinitions.All);
                PrepareSharedWeaponSources(force: true);
                await canceledPreparation.Completion.ContinueWith(task => Require(task.IsCanceled,
                    "replaced source preparation was not canceled"));
                await WaitForSharedWeaponsSmokeAsync();
                Require(_fusionAssets?.Problems.Count == 3, "canceled source work replaced current assets");
                GD.Print("[fusion-smoke] shared lifetime across world exit, source invalidation and canceled-worker isolation PASS");
            }
            finally { _sources = originalSources; }
            GD.Print("[fusion-smoke] PASS: source models/audio/beam, ordinary selector, staged three-weapon damage, occlusion, reset and normal re-entry");
            ApplicationLifecycle.RequestQuit(this, "fusion-smoke-pass");
        }
        catch (Exception error)
        {
            GD.PrintErr("[fusion-smoke] FAIL: " + error);
            ApplicationLifecycle.RequestQuit(this, "fusion-smoke-fail", 3);
        }
        finally
        {
            ClearMovementSmokeInput();
            if (fixture is not null && IsInstanceValid(fixture)) fixture.QueueFree();
        }
    }

    private async Task WaitForSharedWeaponsSmokeAsync()
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (_fusionPreparation is not null && timeout.Elapsed < TimeSpan.FromMinutes(4))
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(_fusionAssets is not null, "shared preparation did not finish: " + _fusionAssetFailure);
    }

    private async Task SelectFusionByInputAsync(int slot)
    {
        FusionKey(Key.Q, true);
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(QuickSelectOpen && _quickSelect!.Entries.Count == 3, "fusion Quick Select did not expose three entries");
        float angle = slot * MathF.PI / 4;
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = MathF.Sin(angle) });
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = -MathF.Cos(angle) });
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(_quickSelect!.SelectedWeapon == FusionWeaponSession.Weapons[slot], "wheel direction selected the wrong source key");
        await CaptureFusionAsync("selector-" + slot);
        FusionKey(Key.Q, false);
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 0 });
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = 0 });
        await PhysicsFramesAsync(3);
        Require(!QuickSelectOpen && _fusion!.Selected == FusionWeaponSession.Weapons[slot], "release did not equip the selected source");
    }

    private async Task FusionFireInputAsync(int frames)
    {
        FusionKey(Key.E, true); await PhysicsFramesAsync(frames); FusionKey(Key.E, false); await PhysicsFramesAsync(2);
    }

    private async Task FusionMarkAsync()
    {
        FusionKey(Key.F8, true); await PhysicsFramesAsync(1); FusionKey(Key.F8, false);
    }

    private static void FusionKey(Key key, bool pressed) => Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });

    private void RequireFusionGunAlignment()
    {
        var key = _fusion!.Selected;
        var basis = _fusionHeldViews[key].GlobalBasis;
        var controller = _playerAvatarAnimationController as OBP.RAC1.Player.Rac1PlayerAnimationPresentationController;
        Require(controller?.SourceEquipmentUseSequence is null && controller?.SourceEquipmentUseWeight == 0,
            $"{key} retained a firing pose while idle");
        RequireFusionHandAttachment();
        Require(MathF.Abs(basis.X.Length() - 1) < .0001f && MathF.Abs(basis.Y.Length() - 1) < .0001f &&
            MathF.Abs(basis.Z.Length() - 1) < .0001f && MathF.Abs(basis.X.Dot(basis.Y)) < .0001f &&
            MathF.Abs(basis.Y.Dot(basis.Z)) < .0001f, $"{key} mount distorts the source proportions");
    }

    private void RequireFusionHandAttachment()
    {
        var key = _fusion!.Selected;
        var controller = (OBP.RAC1.Player.Rac1PlayerAnimationPresentationController)_playerAvatarAnimationController!;
        var hand = controller.CurrentAttachmentPose(OBP.Runtime.Player.PlayerEquipmentPose.HandAttachmentKey);
        Require(hand is not null, $"{key} has no evaluated source hand");
        Vector3 Point(double x, double y, double z) => OBP.Godot.Player.PlayerAvatarView.ToGodotLocal(
            hand!.Transform(new(x, y, z)), _playerAvatar!.Axes, _playerAvatar.BaseHeight, true);
        var origin = Point(0, 0, 0);
        Require(_fusionHeldViews[key].Position.DistanceTo(origin) < .00001f, $"{key} is detached from the native hand origin");
        var expected = new Basis(Point(-1, 0, 0) - origin, Point(0, 0, 1) - origin, Point(0, 1, 0) - origin).Orthonormalized();
        var actual = _fusionHeldViews[key].Basis;
        Require(expected.X.DistanceTo(actual.X) < .00001f && expected.Y.DistanceTo(actual.Y) < .00001f &&
            expected.Z.DistanceTo(actual.Z) < .00001f, $"{key} axes do not follow the animated hand");
    }

    private async Task CheckFusionJumpAttachmentAsync(bool moving)
    {
        var key = _fusion!.Selected;
        var view = _fusionHeldViews[key];
        await WaitForGroundedAsync(_player!, 120);
        SetAnalogueSmokeInput(0, moving ? 1 : 0);
        await PhysicsFramesAsync(moving ? 12 : 2);
        var initial = view.Basis;
        bool sawJump = false, rotated = false;
        int boundaries = 0;
        FusionKey(Key.Space, true);
        for (int tick = 0; tick < 65; tick++)
        {
            if (tick == 16) FusionKey(Key.Space, false);
            var previous = view.Transform;
            var previousSkin = _playerAvatarAnimationController!.Current.SampledPose?.LocalPositions;
            await PhysicsFramesAsync(1);
            RequireFusionHandAttachment();
            RequireFusionTransitionContinuity(previous, previousSkin, ref boundaries);
            if (_playerAvatarAnimationController is OBP.RAC1.Player.Rac1PlayerAnimationPresentationController { TraversalSample.Sequence: 7 or 8 })
            {
                sawJump = true;
                rotated |= initial.X.DistanceTo(view.Basis.X) > .01f || initial.Z.DistanceTo(view.Basis.Z) > .01f;
                if (tick == 15) await CaptureFusionAsync($"{SourceIdentityWire.FormatSource(key.SourceGame)}-{(moving ? "moving" : "standing")}-jump");
            }
        }
        FusionKey(Key.Space, false);
        ClearMovementSmokeInput();
        await WaitForGroundedAsync(_player!, 180);
        Require(sawJump && rotated, $"{key} {(moving ? "moving" : "standing")} jump retained locked weapon axes");
        Require(boundaries >= 1, $"{key} jump did not exercise a pose transition");
        GD.Print($"[fusion-smoke] {key} {(moving ? "moving" : "standing")} jump hand position and axes PASS");
    }

    private void RequireFusionTransitionContinuity(Transform3D previous, double[]? previousSkin, ref int boundaries)
    {
        var controller = (OBP.RAC1.Player.Rac1PlayerAnimationPresentationController)_playerAvatarAnimationController!;
        if (!controller.SourceEquipmentIsBlending || controller.SourceEquipmentBlendFraction != 0 || previousSkin is null) return;
        boundaries++;
        var actual = _fusionHeldViews[_fusion!.Selected].Transform;
        Require(actual.Origin.DistanceTo(previous.Origin) < .00001f &&
            actual.Basis.X.DistanceTo(previous.Basis.X) < .00001f && actual.Basis.Y.DistanceTo(previous.Basis.Y) < .00001f &&
            actual.Basis.Z.DistanceTo(previous.Basis.Z) < .00001f, $"{_fusion.Selected} gun snapped at a transition boundary");
        var skin = controller.Current.SampledPose!.LocalPositions;
        Require(skin.Zip(previousSkin, (a, b) => Math.Abs(a - b)).Max() < .00001,
            $"{_fusion.Selected} arms/body did not retain the rendered source pose");
    }

    private async Task CheckFusionMovementTransitionsAsync()
    {
        int boundaries = 0; var sequences = new HashSet<int>();
        foreach (var phase in new (bool Move, int Ticks)[] { (false, 20), (true, 45), (false, 90), (true, 45), (false, 6), (true, 30), (false, 90) })
        {
            SetAnalogueSmokeInput(0, phase.Move ? 1 : 0);
            for (int tick = 0; tick < phase.Ticks; tick++)
            {
                var previous = _fusionHeldViews[_fusion!.Selected].Transform;
                var skin = _playerAvatarAnimationController!.Current.SampledPose?.LocalPositions;
                await PhysicsFramesAsync(1);
                RequireFusionHandAttachment();
                RequireFusionTransitionContinuity(previous, skin, ref boundaries);
                if (CurrentPlayerAvatarSourceSequence() is int sequence) sequences.Add(sequence);
            }
        }
        ClearMovementSmokeInput();
        Require(boundaries >= 4 && sequences.Contains(0) && sequences.Any(s => s is 3 or 4) && sequences.Any(s => s is 5 or 6 or 20),
            $"{_fusion!.Selected} transition coverage incomplete: {boundaries} boundaries, sequences {string.Join(',', sequences)}");
        GD.Print($"[fusion-smoke] {_fusion!.Selected} idle/run/skid/stop and interrupted transitions PASS ({boundaries} boundaries)");
    }

    private async Task FusionCheckWrenchPersistenceAsync()
    {
        var key = _fusion!.Selected;
        int ammo = _fusion.Status(key).Ammo;
        FusionKey(Key.X, true);
        await Rac1SmokeWaitAsync(() => _player!.Rac1WrenchFrame.Active, 20, "foreign equipment Square handoff");
        FusionKey(Key.X, false);
        await Rac1SmokeWaitAsync(() => !_player!.Rac1WrenchFrame.Active, 120, "foreign equipment Square recovery");
        await PhysicsFramesAsync(30);
        Require(_rac1HeldItem.Hand.WrenchHeld && _rac1WrenchView!.Visible && !_fusionHeldViews[key].Visible &&
            _fusion.Selected == key && _fusion.Status(key).Ammo == ammo, $"{key} did not retain the wrench and selected equipment independently");
        await CaptureFusionAsync(key.SourceGame + "-wrench-retained");
        await FusionFireInputAsync(1);
        await PhysicsFramesAsync(2);
        Require(_rac1HeldItem.Hand.IsHolding(key) && _fusionHeldViews[key].Visible && !_rac1WrenchView!.Visible,
            $"{key} was not drawn by Circle");
        GD.Print($"[fusion-smoke] {key} persistent wrench hand and Circle redraw PASS");
    }

    private async Task CaptureFusionProfileAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("OBP_FUSION_CAPTURE_DIR")) ||
            DisplayServer.GetName() == "headless" || _playtest is not null) return;
        var camera = _activeCamera;
        var transform = camera.GlobalTransform;
        bool paused = GetTree().Paused;
        try
        {
            GetTree().Paused = true;
            Vector3 target = _player!.GlobalPosition + Vector3.Up * .85f + EquipmentForward * .3f;
            camera.GlobalPosition = target + EquipmentForward.Cross(Vector3.Up) * 2.2f - EquipmentForward * .5f + Vector3.Up * .4f;
            camera.LookAt(target);
            await CaptureFusionAsync(name);
        }
        finally { camera.GlobalTransform = transform; GetTree().Paused = paused; }
    }

    private async Task CaptureFusionAsync(string name)
    {
        string? directory = System.Environment.GetEnvironmentVariable("OBP_FUSION_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory) || DisplayServer.GetName() == "headless") return;
        Directory.CreateDirectory(directory);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Require(image.SavePng(Path.Combine(directory, name + ".png")) == Error.Ok, "fusion capture failed");
        File.WriteAllText(Path.Combine(directory, name + ".pose.json"), JsonSerializer.Serialize(new
        {
            selected = _fusion?.Selected.ToString(),
            heldEquipment = _rac1HeldItem.Hand.Equipment?.ToString() ?? "wrench",
            sourceSequence = CurrentPlayerAvatarSourceSequence(),
            sourceUseSequence = (_playerAvatarAnimationController as OBP.RAC1.Player.Rac1PlayerAnimationPresentationController)?.SourceEquipmentUseSequence,
            sourceUseWeight = (_playerAvatarAnimationController as OBP.RAC1.Player.Rac1PlayerAnimationPresentationController)?.SourceEquipmentUseWeight,
            sourceBlendFraction = (_playerAvatarAnimationController as OBP.RAC1.Player.Rac1PlayerAnimationPresentationController)?.SourceEquipmentBlendFraction,
            forward = new[] { EquipmentForward.X, EquipmentForward.Y, EquipmentForward.Z },
            weapons = _fusionHeldViews.Select(pair => new
            {
                key = pair.Key.ToString(),
                pair.Value.Visible,
                transform = new[] { pair.Value.GlobalBasis.X, pair.Value.GlobalBasis.Y, pair.Value.GlobalBasis.Z, pair.Value.GlobalPosition }
                    .Select(v => new[] { v.X, v.Y, v.Z }),
                localBounds = pair.Value.GetChildren().OfType<MeshInstance3D>().Select(m => new
                {
                    position = new[] { m.GetAabb().Position.X, m.GetAabb().Position.Y, m.GetAabb().Position.Z },
                    size = new[] { m.GetAabb().Size.X, m.GetAabb().Size.Y, m.GetAabb().Size.Z }
                })
            })
        }));
    }
}
