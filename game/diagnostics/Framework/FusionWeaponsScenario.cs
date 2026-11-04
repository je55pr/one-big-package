using System.Numerics;
using OBP.Runtime.Gameplay;

namespace OBP.Game.Diagnostics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "S1244", Justification = "Exact native damage constants and source inventory witnesses preserve the original acceptance comparisons.")]
public sealed class FusionWeaponsScenario(MovementDiagnosticContext context, Rac1WeaponScenarioPorts ports, IFusionWeaponDiagnosticFixture fusion)
    : FusionWeaponScenarioSupport(context, ports, fusion)
{
    public async Task RunAsync()
    {
        IDiagnosticGeometryLease? fixture = null;
        try
        {
            using var frameRate = World.LimitFrameRate(60);
            long preparationFrame = Fusion.ReadStatus().Frame;
            bool wasPreparing = Fusion.ReadStatus().Preparing;
            await WaitForSharedWeaponsSmokeAsync();
            Require(!wasPreparing || Fusion.ReadStatus().Frame > preparationFrame, "source preparation blocked application frames");
            Require(Fusion.ReadStatus().Present && Fusion.ReadStatus().Problems == 0, "fusion smoke requires three verified sources on Veldin");
            ClearMovementSmokeInput();
            Log($"[fusion-smoke] shared source preparation yielded {Fusion.ReadStatus().Frame - preparationFrame} application frames");
            string normalInventory = Fusion.OriginalInventory();
            await WaitForGroundedAsync(Player, 180);
            await SelectFusionByInputAsync(1);
            await PhysicsFramesAsync(30);
            RequireFusionGunAlignment();
            await CaptureFusionProfileAsync("lancer-profile");
            Require(Fusion.ReadHeld(FusionWeaponSession.Lancer).HasMeshes, "original Lancer mesh absent");
            Require(!Ports.Bomb.ReadView().WrenchVisible, "Lancer retained the R&C1 wrench model");
            int beforeWrench = Fusion.ReadSession().LancerAmmo;
            FusionKey(DiagnosticPhysicalKey.X, true);
            await Rac1SmokeWaitAsync(() => PlayerWrenchActive(), 20, "Square wrench input");
            Require(Ports.Bomb.ReadView().WrenchVisible && !Fusion.ReadHeld(FusionWeaponSession.Lancer).Visible, "Square did not own the hand model");
            FusionKey(DiagnosticPhysicalKey.X, false);
            await Rac1SmokeWaitAsync(() => !PlayerWrenchActive(), 120, "Square recovery");
            await PhysicsFramesAsync(2);
            Require(Fusion.ReadSession().LancerAmmo == beforeWrench && Ports.Bomb.ReadView().WrenchVisible &&
                !Fusion.ReadHeld(FusionWeaponSession.Lancer).Visible && Fusion.ReadSession().WrenchHeld,
                "Square did not leave the wrench equipped after recovery");
            await PhysicsFramesAsync(30);
            Require(Ports.Bomb.ReadView().WrenchVisible && !Fusion.ReadHeld(FusionWeaponSession.Lancer).Visible,
                "selected foreign equipment reclaimed the idle hand");
            await FusionFireInputAsync(1); // Circle explicitly draws the selected equipment again.
            Require(Fusion.ReadHeld(FusionWeaponSession.Lancer).Visible && !Ports.Bomb.ReadView().WrenchVisible,
                $"Circle did not return hand ownership to selected equipment: hand={Fusion.ReadSession().Held}, " +
                $"wrench={PlayerWrenchActive()}, hanging={Player.Read().Rac1Hanging}, tether={Player.Read().Rac1SwingshotActive}, alive={Player.Read().Rac1GameplayAlive}");
            await SelectFusionByInputAsync(2);
            await PhysicsFramesAsync(30);
            RequireFusionGunAlignment();
            await CaptureFusionProfileAsync("cannon-profile");
            Require(Fusion.ReadHeld(FusionWeaponSession.ShockCannon).HasMeshes, "original Cannon mesh absent");
            await SelectFusionByInputAsync(0);
            await CaptureFusionAsync("ordinary-selector");
            Log("[fusion-smoke] three source-qualified entries, original held assets and ordinary selector input PASS");
            var attachedAssets = Fusion.CaptureAssets();
            using (Fusion.UseUnavailableAssets())
            {

                try
                {
                    Require(Fusion.ReadStatus().Problems == 3, "missing sources were silently admitted");
                    await SelectFusionByInputAsync(1);
                    int unavailableAmmo = Fusion.ReadSession().LancerAmmo;
                    await FusionFireInputAsync(20);
                    Require(Fusion.ReadSession().LancerAmmo == unavailableAmmo && !Fusion.ReadHeld(FusionWeaponSession.Lancer).Visible &&
                        Ports.Additional.Read().Hud?.Status?.Contains("Game Sources", StringComparison.Ordinal) == true,
                        "missing source fired, displayed a model or lacked actionable status");
                    await CaptureFusionAsync("missing-source");
                }
                finally { ClearMovementSmokeInput(); }
            }
            await SelectFusionByInputAsync(0);

            fixture = Geometry.Create("FusionStagedContactFixture", new(-2000, 64, 2600));
            fixture.AddMesh("FusionFloor", 11,
                (new(-30, 0, -30), new(30, 0, -30), new(30, 0, 30)),
                (new(-30, 0, -30), new(30, 0, 30), new(-30, 0, 30)));
            await PhysicsFramesAsync(3);
            Player.ResetAt(fixture.GlobalPosition + new Vector3(6, .05f, 0));
            await WaitForGroundedAsync(Player, 120);
            Ports.Combat.PlacePlayerFacing(Player.Read().GlobalPosition, -Vector3.UnitX, true);
            await PhysicsFramesAsync(10);
            foreach (int selection in new[] { 1, 2 })
            {
                Player.ResetAt(fixture.GlobalPosition + Vector3.UnitY * .05f);
                await WaitForGroundedAsync(Player, 120);
                await SelectFusionByInputAsync(selection);
                foreach (bool movingJump in new[] { false, true }) await CheckFusionJumpAttachmentAsync(movingJump);
                await CheckFusionMovementTransitionsAsync();
                await FusionCheckWrenchPersistenceAsync();
            }
            await SelectFusionByInputAsync(0);
            Player.ResetAt(fixture.GlobalPosition + new Vector3(6, .05f, 0));
            await WaitForGroundedAsync(Player, 120);
            Ports.Combat.PlacePlayerFacing(Player.Read().GlobalPosition, -Vector3.UnitX, true);
            await PhysicsFramesAsync(10);
            var crates = Targets.ReadCrates().Where(n => n.Key.Target.NativeClassId == 500 && Targets.Read(n.Key).Active).Take(5).ToArray();
            Require(crates.Length == 5, "five authored crate objects required");
            await FusionFireInputAsync(3);
            await Rac1SmokeWaitAsync(() => Ports.Combat.ReadBombs().Projectiles.Length != 0 && !Ports.Bomb.CaptureSingleShot().Read().Flying, 180, "Bomb Glove floor impact");
            Vector3 landing = FromEffectNative(Ports.Bomb.CaptureSingleShot().Read().NativePosition);
            await PhysicsFramesAsync(120);
            Targets.StageCrate(crates[0].Key, new(landing.X, fixture.GlobalPosition.Y, landing.Z));
            await PhysicsFramesAsync(3);
            await FusionFireInputAsync(3);
            await Rac1SmokeWaitAsync(() => !Targets.Read(crates[0].Key).Active, 180, "Bomb Glove authored crate reaction");
            Require(Ports.Equipment.Read().BombAmmo == 8, "Bomb Glove staged ammo");
            await PhysicsFramesAsync(120);

            await SelectFusionByInputAsync(1);
            Targets.StageCrate(crates[1].Key, Player.Read().GlobalPosition + EquipmentForward * 4);
            await PhysicsFramesAsync(3);
            await FusionFireInputAsync(30);
            Require(!Targets.Read(crates[1].Key).Active && Fusion.ReadSession().LastDamage is { SourceDamage: .5f } &&
                Fusion.ReadSession().LastDamage?.Weapon == FusionWeaponSession.Lancer, "Lancer half-damage crate adapter");
            await CaptureFusionAsync("lancer-contact");
            await PhysicsFramesAsync(30); // Drain shots already past the broken crate before placing the robot.

            var robot = Targets.ReadEnemies().FirstOrDefault(e => e.Key.Target.NativeClassId == 1440 && !e.RewardEmitted)
                ?? throw new DiagnosticUnavailableException("The fusion damage fixture requires an original class1440 robot.");
            Targets.StageEnemy(robot.Key, Player.Read().GlobalPosition + EquipmentForward * 5);
            await PhysicsFramesAsync(7);
            float health = Targets.ReadRobotHealth(robot.Key);
            await FusionFireInputAsync(25);
            float after = Targets.ReadRobotHealth(robot.Key);
            Require(after == health - .5f && !Targets.Read(robot.Key).RewardEmitted, $"Lancer fractional robot reaction/cooldown: {health}->{after}");
            Log("[fusion-smoke] Bomb Glove / Lancer original ammo and authored crate/robot consequences PASS");

            await SelectFusionByInputAsync(2);
            Targets.StageCrate(crates[2].Key, Player.Read().GlobalPosition + EquipmentForward * 3);
            await PhysicsFramesAsync(3);
            await FusionFireInputAsync(10);
            Require(!Targets.Read(crates[2].Key).Active && Fusion.ReadSession().LastDamage?.Weapon == FusionWeaponSession.ShockCannon, "Shock spread crate reaction");
            await PhysicsFramesAsync(30);
            FusionKey(DiagnosticPhysicalKey.E, true);
            await PhysicsFramesAsync(15);
            Targets.StageCrate(crates[3].Key, Player.Read().GlobalPosition + EquipmentForward * 4);
            await Rac1SmokeWaitAsync(() => Fusion.ReadSession().BeamUpdatesRemaining > 0, 90, "charged beam");
            Require(!Targets.Read(crates[3].Key).Active && Fusion.ReadSession().LastDamage is { SourceDamage: 50, Kind: "beam" }, "charged raw-damage crate reaction");
            Require(Fusion.ReadSession().BeamVisible && Fusion.ReadSession().AudioStarted > 4, "source beam/audio feedback");
            await CaptureFusionAsync("charged-beam");
            FusionKey(DiagnosticPhysicalKey.E, false);
            await PhysicsFramesAsync(45);

            // A wall must consume a projectile before a later eligible target.
            await SelectFusionByInputAsync(1);
            var wall = Driver.Own(fixture.AddBox("FusionOcclusionWall", new(.15f, 6, 8), Player.Read().GlobalPosition + EquipmentForward * 2 + Vector3.UnitY * 2, 0));
            Targets.StageCrate(crates[4].Key, Player.Read().GlobalPosition + EquipmentForward * 5);
            await PhysicsFramesAsync(3);
            await FusionFireInputAsync(25);
            Require(Targets.Read(crates[4].Key).Active, "Lancer hit an authored crate through blocking world geometry");
            wall.Dispose();
            Fusion.ClearTransients();
            await PhysicsFramesAsync(3);
            Require(Fusion.ReadTransients().Projectiles == 0 && Fusion.ReadTransients().LancerShots == 0 && Fusion.ReadTransients().Effects == 0 &&
                Fusion.ReadTransients().PendingAudio == 0 && Fusion.ReadTransients().AudioPlayers == 0 && Ports.Combat.ReadBombs().Projectiles.Length == 0,
                "fusion reset retained projectiles/effects/audio");
            Require(Fusion.OriginalInventory() == normalInventory, "fusion contaminated campaign inventory");

            Require(Fusion.ReenterWithoutIsolatedLoadout(), "normal Veldin re-entry rejected");
            await PhysicsFramesAsync(5);
            Require(Fusion.ReadStatus().Present && !Fusion.ReadStatus().Isolated && attachedAssets.IsCurrent &&
                !Fusion.ReadStatus().Preparing && Fusion.HeldModelCount == 2,
                "world re-entry discarded shared assets or retained isolated session ownership");
            Require(Fusion.CurrentInventory() == normalInventory, "normal inventory was not restored");
            Require(Fusion.ReadSession().NativeInventoryAlias, "default weapons replaced the campaign inventory");
            FusionKey(DiagnosticPhysicalKey.Q, true);
            for (int i = 0; i < 3; i++) await ProcessFrameAsync();
            Require(Fusion.ReadMenu().Entries.Any(e => e == FusionWeaponSession.Lancer) &&
                Fusion.ReadMenu().Entries.Any(e => e == FusionWeaponSession.ShockCannon), "default Quick Select omitted the two foreign weapons");
            FusionKey(DiagnosticPhysicalKey.Q, false);
            await PhysicsFramesAsync(2);
            await SelectFusionByInputAsync(1);
            Require(!Fusion.ReadStatus().Preparing && attachedAssets.IsCurrent, "selection restarted source processing");
            Require(!Fusion.ReadStatus().Isolated && Fusion.ReadStatus().Loadout == FusionWeaponSession.DefaultLoadoutId,
                "ordinary selection entered the isolated test loadout");
            await PhysicsFramesAsync(3);
            Require(Fusion.ReadHeld(FusionWeaponSession.Lancer).HasMeshes,
                "background preparation did not attach the original Lancer model");
            Log("[fusion-smoke] default first equip reuses prepared shared models, textures and audio PASS");
            await FusionFireInputAsync(20);
            Require(Fusion.ReadSession().LancerAmmo < 40, "default Lancer did not fire");
            await SelectFusionByInputAsync(2);
            await FusionFireInputAsync(12);
            Require(Fusion.ReadSession().CannonAmmo < 20, "default Cannon did not fire");
            await SelectFusionByInputAsync(0);
            Require(Fusion.CurrentInventory() == normalInventory &&
                Fusion.ReadSession().CampaignInventoryAlias, "default weapons replaced or modified native inventory");
            Log("[fusion-smoke] default Quick Select, both foreign weapons and normal native inventory PASS");
            Fusion.ShowDestinations();
            Require(attachedAssets.IsCurrent, "world exit discarded shared equipment");
            using var originalSources = Fusion.IsolateSources();
            try
            {
                originalSources.DetachAll();
                Fusion.PrepareSources();
                await WaitForSharedWeaponsSmokeAsync();
                Require(Fusion.ReadStatus().Problems == 3 && !attachedAssets.IsCurrent,
                    "source change retained stale verified assets");
                originalSources.RestoreAll();
                Fusion.PrepareSources();
                var canceledPreparation = Fusion.CapturePreparation();
                Fusion.ResetWorldWeapons();
                Require(canceledPreparation.IsCurrent, "level reset canceled shared preparation");
                originalSources.DetachAll();
                Fusion.PrepareSources();
                await canceledPreparation.RequireCanceledAsync(Driver.Lifetime);
                await WaitForSharedWeaponsSmokeAsync();
                Require(Fusion.ReadStatus().Problems == 3, "canceled source work replaced current assets");
                Log("[fusion-smoke] shared lifetime across world exit, source invalidation and canceled-worker isolation PASS");
            }
            finally { originalSources.RestoreAll(); }
            Log("[fusion-smoke] PASS: source models/audio/beam, ordinary selector, staged three-weapon damage, occlusion, reset and normal re-entry");

        }
        catch (OperationCanceledException error) when (Driver.Lifetime.IsCancellationRequested && error.CancellationToken == Driver.Lifetime)
        {
            throw;
        }
        catch (Exception error)
        {
            LogError("[fusion-smoke] FAIL: " + error);
            throw;
        }
        finally
        {
            ClearMovementSmokeInput();
            CleanupStep(() => fixture?.Dispose());
        }
    }


}
