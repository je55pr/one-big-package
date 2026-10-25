using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.IO;
using OBP.RAC1.Player;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    private sealed record CapabilitySample(int Tick, string? Sequence, int? Frame, string Pose, string Attachment,
        double Facing, double[] Position, double[] Camera, double Clock);

    /// <summary>Explicit common-contact fixture in four real world leases; no native-world parity claim for the artificial deck.</summary>
    private async Task RunPlayerCapabilitiesSmokeAsync()
    {
        try
        {
            Require(OrdinaryPlayerSession && _playerNative is not null, "ordinary session required");
            var native = _playerNative!; var owner = _playerSession!;
            var initial = owner.Capture();
            var configuration = initial.Configuration;
            string identity = PlayerEquipmentConfigurationIdentity!;
            MovementReproPlayer? canonical = null;
            RuntimeWorldScene.DynamicObjectNode? retiredActor = null;
            var baselines = new Dictionary<string, CapabilitySample[]>();
            foreach (string id in new[] { "rac1:LEVEL0", "rac2:LEVEL1", "rac3:TABLE1", "obp:fusion-lab", "rac1:LEVEL0" })
            {
                owner.Restore(initial);
                Require(id == "obp:fusion-lab" ? EnterFusionLab() : OnDestinationChosen(TrilogyWorldProviders.Resolve(id)!), "entry failed " + id);
                await PhysicsFramesAsync(3);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (retiredActor is not null)
                    Require(ApplyPlayerSourceDamage(retiredActor, OBP.RAC1.Rac1Authority.Primary.BuildId,
                        PlayerSessionNativeState.Rac1DamageAdapter, PlayerSessionNativeState.Rac1DamageDescriptor, 999999, 1, 0).Status == PlayerIntakeStatus.StaleLease,
                        "old scene's actor callback was rebound to the new health lease");
                retiredActor = _rac1HostileNodes.Values.FirstOrDefault();
                var player = _player!;
                Require(player.ControlProfile == PlayerHostProfiles.Native && player.HasActiveRecoveredCamera, "profile/camera changed with world");
                Require(_playerAvatarView is not null && _playerAvatar!.Identity == configuration.Avatar, "genuine selected avatar missing");
                Require(_hudState.Current.Health is not null && _hudState.Current.Bolts is not null && _playerHud!.Visible, "shared HUD hidden");
                Require(PlayerEquipmentConfigurationIdentity == identity, "world changed catalog configuration identity");
                Require(_playerRoot.GetChildCount() == 1 && _worldRoot.GetChildCount() == 1, "duplicate world/player after re-entry");
                var deck = new StaticBody3D { Name = "ExplicitCommonContactFixture" };
                deck.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(200, 2, 200) } });
                _sceneResult!.Root.AddChild(deck); deck.GlobalPosition = new(4096, 4095, 4096);
                await PhysicsFramesAsync(2);
                player.PlaytestInputFilter = _ => default;
                player.Rotation = Vector3.Zero; player.Rac1CurrentYaw = 0;
                player.ApplyDevelopmentReset(new(4096, 4097, 4096));
                await PhysicsFramesAsync(40);
                canonical ??= player.CaptureMovementRepro();
                foreach (var weapon in new[] { FusionWeaponSession.Lancer, FusionWeaponSession.ShockCannon })
                {
                    player.RestoreMovementRepro(canonical);
                    SelectWeaponKey(weapon);
                    Require(_fusion!.Hand.IsHolding(weapon), "supported foreign weapon did not draw");
                    var trace = await CaptureCapabilityTrialAsync(player);
                    string key = weapon.ToString();
                    if (baselines.TryGetValue(key, out var baseline))
                        Require(JsonSerializer.Serialize(trace) == JsonSerializer.Serialize(baseline), "movement/pose/attachment/camera trace differs in " + id + " for " + key);
                    else baselines.Add(key, trace);
                    Require(trace.Select(sample => sample.Sequence).Distinct().Count() >= 3, "fixture did not exercise native sequence transitions");
                    Require(trace[^1].Clock == 4, "240 native ticks did not take four native seconds");
                    Require(_fusion.Selected == weapon && _rac1HeldItem.Hand.WrenchHeld, "wrench attack did not retain hand ownership");
                    GD.Print($"[player-capabilities-smoke] {id} {weapon}: matching 240 ticks, movement, camera, pose, attachments and wrench ownership");
                }
                player.PlaytestInputFilter = _ => default;
                await CheckCapabilityMenuMaskAsync(player);
            }
            Require(AssignPlayerQuickSelect(7, "rac2/item/30").Accepted, "foreign Quick Select assignment failed");
            Require(native.Assignments.Capture().QuickSelect[7] == FusionWeaponSession.Lancer, "foreign assignment lost identity");
            Require(TryOpenWeaponQuickSelect(new InputEventAction { Action = ControllerBindings.QuickSelect, Pressed = true }), "assigned wheel unavailable");
            Require(_quickSelect!.Entries[7].Weapon == FusionWeaponSession.Lancer && _quickSelect.Entries[1].Weapon is null, "wheel compacted a cleared slot or moved a foreign slot");
            CloseWeaponQuickSelect();
            Require(SetPlayerGadgetEnabled("rac1/item/3", true).Accepted && !native.GadgetEnabled(OBP.RAC1.Player.Rac1TraversalEquipmentId.HeliPack), "conflicting packs enabled");
            Require(!SelectPlayerEquipment("rac2/appearance/ratchet").Accepted && !SetPlayerGadgetEnabled("rac1/item/28", true).Accepted, "unsupported skin/gadget admitted");
            await CheckCapabilitySourceRecoveryAsync();
            await CheckCapabilityHealthAsync();
            if (_args.PlayerCapabilitiesOut is { } output)
                System.IO.File.WriteAllText(output, JsonSerializer.Serialize(new { Version = 1, Baselines = baselines }));
            GD.Print("[player-capabilities-smoke] PASS: world-independent profiles, native clock/poses, camera, input masking, source recovery, catalog and shared health");
            ApplicationLifecycle.RequestQuit(this, "player-capabilities-smoke-pass", 0);
        }
        catch (Exception error)
        {
            GD.PrintErr("[player-capabilities-smoke] FAIL: " + error);
            ApplicationLifecycle.RequestQuit(this, "player-capabilities-smoke-fail", 3);
        }
    }

    private async Task<CapabilitySample[]> CaptureCapabilityTrialAsync(PlayerHost player)
    {
        var samples = new List<CapabilitySample>();
        var done = new TaskCompletionSource();
        int inputTick = 0;
        long serviceStart = _playerServiceTick;
        player.PlaytestInputFilter = _ =>
        {
            int tick = inputTick++;
            return new RawPlayerInputFrame
            {
                Move = tick is >= 20 and < 70 ? new(0, -.6f) : tick is >= 70 and < 110 or >= 160 and < 190 ? new(0, -1) :
                    tick is >= 110 and < 140 ? new(.65f, -.8f) : Vector2.Zero,
                JumpHeld = tick is >= 120 and < 130,
                CrouchHeld = tick is >= 175 and < 190,
                ActionJustPressed = tick == 210,
                ActionHeld = tick is >= 210 and < 215,
                CameraIntent = tick is >= 90 and < 105 ? new(.3f, .1f) : Vector2.Zero,
            };
        };
        void Sample()
        {
            try
            {
                Require(_playerPhysicsClock!.Tick == inputTick && _playerServiceTick - serviceStart == inputTick, "duplicate/missing player physics subscription");
                if (inputTick % 6 == 0)
                {
                    var pose = _playerAvatarAnimationController!.Current;
                    var controller = (Rac1PlayerAnimationPresentationController)_playerAvatarAnimationController;
                    string attachment = JsonSerializer.Serialize(controller.CurrentAttachmentPose(Rac1WrenchAttachment.SocketKey));
                    var camera = player.Camera.GlobalTransform;
                    samples.Add(new(inputTick, pose.SourceSequenceKey, pose.SampledPose?.FrameIndex, PoseDigest(pose.SampledPose?.LocalPositions ?? []), attachment,
                        player.Rac1CurrentYaw, Coordinates(player.GlobalPosition), Coordinates(camera.Origin), _playerPhysicsClock.Seconds));
                }
                if (inputTick >= 240) done.TrySetResult();
            }
            catch (Exception error) { done.TrySetException(error); }
        }
        player.DevelopmentPhysicsCompleted += Sample;
        try { await done.Task; }
        finally { player.DevelopmentPhysicsCompleted -= Sample; player.PlaytestInputFilter = _ => default; }
        return samples.ToArray();
    }

    private static double[] Coordinates(Vector3 value) => [value.X, value.Y, value.Z];
    private static string PoseDigest(double[] values)
    {
        var bytes = new byte[values.Length * sizeof(double)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private async Task CheckCapabilityMenuMaskAsync(PlayerHost player)
    {
        var selected = _fusion!.Selected;
        Require(TryOpenWeaponQuickSelect(new InputEventAction { Action = ControllerBindings.QuickSelect, Pressed = true }), "Quick Select unavailable");
        int shots = _fusion.ShotsFired;
        long wrenchGeneration = player.Rac1WrenchFrame.Generation;
        player.PlaytestInputFilter = _ => new RawPlayerInputFrame { JumpHeld = true, ActionJustPressed = true, ActionHeld = true, TraversalHeld = true, Move = Vector2.One };
        await PhysicsFramesAsync(8);
        Require(_fusion.ShotsFired == shots && player.Rac1WrenchFrame.Generation == wrenchGeneration && _fusion.Selected == selected, "Quick Select leaked attack input");
        CloseWeaponQuickSelect();
        player.PlaytestInputFilter = _ => default;
        Require(TryOpenPauseMenu(), "pause unavailable");
        long clock = _playerPhysicsClock!.Tick;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(_playerPhysicsClock.Tick == clock, "pause advanced native player clock");
        ClosePauseMenu();
    }

    private async Task CheckCapabilitySourceRecoveryAsync()
    {
        Require(SelectPlayerEquipment("rac2/item/30").Accepted, "Lancer selection failed");
        var source = _sources.Get(ObpSourceGame.Rac2)!;
        _sources.Remove(ObpSourceGame.Rac2); RefreshPlayerSourceAvailability();
        var entry = ReadPlayerEquipment().Single(entry => entry.Definition.Item == FusionWeaponSession.Lancer);
        Require(entry.Usability == EquipmentUsability.MissingSource && _fusion!.Selected == FusionWeaponSession.Lancer && _fusion.Hand.WrenchHeld,
            "source removal changed desired loadout or did not use safe wrench fallback");
        using (var reader = new FileRandomAccessReader(source.Path)) _sources.Restore(new(source.Game, source.Identity.BuildId, source.Path), reader);
        RefreshPlayerSourceAvailability();
        Require(SelectPlayerEquipment("rac2/item/30").Accepted && _fusion!.Hand.IsHolding(FusionWeaponSession.Lancer), "source recovery could not re-equip remembered weapon");
        await PhysicsFramesAsync(1);
    }

    private async Task CheckCapabilityHealthAsync()
    {
        Require(OnDestinationChosen(TrilogyWorldProviders.Resolve("rac2:LEVEL0")!), "Aranos source combat entry failed");
        await PhysicsFramesAsync(3);
        var native = _playerNative!;
        Require(_rac2OpeningMsr1.Count > 0, "no admitted GC attack controller");
        var hostile = _rac2OpeningMsr1.Values.OrderBy(session => session.Hostile.InstanceIndex).First();
        _player!.PlaytestInputFilter = _ => default;
        _player.ApplyDevelopmentReset(hostile.Node.Root.GlobalPosition + new Vector3(1, 0, 0));
        for (int i = 0; i < 600 && native.Life.Capture().Current == 4; i++) await PhysicsFramesAsync(1);
        Require(native.Life.Capture().Current < 4 && _rac2RatchetNanotech.Current == 4, "native GC hostile did not damage the selected shared victim");
        Require(_hudState.Current.Health?.Current == native.Life.Capture().Current, "GC hit did not project into the shared HUD");
        GD.Print("[player-capabilities-smoke] source GC MSR I attack reached the shared R&C1-derived victim/HUD");
        Require(EnterFusionLab(), "health lifecycle Lab entry failed");
        var before = _playerSession!.Capture();
        native.Life.DefeatFromWorldHazard(); await PhysicsFramesAsync(2);
        Require(!native.Life.Alive && !_player!.Rac1GameplayAlive && _hudState.Current.Health!.LifeState == OBP.Runtime.Presentation.HudLifeState.Dead, "death not shared");
        var avatarSource = _sources.Get(ObpSourceGame.Rac1)!;
        var liveWorld = _world;
        _sources.Remove(ObpSourceGame.Rac1);
        try
        {
            Require(!RespawnPlayer() && !native.Life.Alive && ReferenceEquals(liveWorld, _world), "failed respawn consumed the retry or destroyed the live scene");
        }
        finally
        {
            using var reader = new FileRandomAccessReader(avatarSource.Path);
            _sources.Restore(new(avatarSource.Game, avatarSource.Identity.BuildId, avatarSource.Path), reader);
            RefreshPlayerSourceAvailability();
        }
        Require(RespawnPlayer(), "OBP respawn failed"); await PhysicsFramesAsync(2);
        Require(native.Life.Alive && native.Life.Capture().Current == 4, "respawn did not restore selected capacity");
        Require(_playerSession.Capture().Loadout.SelectedWeapon == before.Loadout.SelectedWeapon && native.Wallet.Balance == before.Currencies["obp/bolts"], "respawn reset equipment/currency");
    }
}
