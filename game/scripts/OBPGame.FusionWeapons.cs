using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Progression;
using OBP.RAC2.Gameplay;
using OBP.RAC3.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Audio;
using OBP.Runtime.Diagnostics;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;
using OBP.Runtime.Presentation;
using NVector3 = System.Numerics.Vector3;

namespace OneBigPackage;

public partial class OBPGame
{
    private FusionWeaponSession? _fusion;
    private FusionWeaponAssets? _fusionAssets;
    private FusionWeaponPreparation? _fusionPreparation;
    private string? _fusionLoadingStatus, _fusionAssetFailure;
    private ObpAttachedSource[]? _fusionSourceSnapshot;
    private CanvasLayer? _fusionPreparationLayer;
    private Label? _fusionPreparationNotice;
    private Rac1CampaignRuntimeSession? _fusionOriginalCampaign;
    private Rac1BoltWallet? _fusionOriginalWallet;
    private int[] _fusionOriginalGoldBolts = [];
    private readonly Dictionary<SourceWeaponKey, FusionMeshView> _fusionHeldViews = [];
    private readonly Dictionary<WeaponShotKey, FusionMeshView> _fusionProjectiles = [];
    private readonly List<(int Due, int Owner, FusionWeaponData.Voice Voice)> _fusionPendingAudio = [];
    private readonly List<(int Owner, AudioStreamPlayer Player)> _fusionAudioPlayers = [];
    private readonly List<(Node3D Node, int Remaining)> _fusionEffects = [];
    private FusionMeshView? _fusionBeam;
    private UyaShockCannonBeam.Scroll _fusionBeamScroll;
    private int _fusionModelAge, _fusionModelSequence, _fusionAcceptedHits, _fusionAudioStarted;
    private bool _fusionUseStarted, _fusionPoseTriggerHeld;
    private bool _fusionDeathCleared;
    private WeaponDamageIntent? _fusionLastDamage;
    private WeaponStatus? _fusionDisplayedStatus;
    private SourceWeaponKey? _fusionDisplayedHand;
    private bool FusionTestActive => _fusionOriginalCampaign is not null;
    // An explicit original-world test fixture, never ordinary equipment admission.
    private bool CanRunNativeFusionFixture => !OrdinaryPlayerSession && _activeDestination?.DestinationId == "rac1:LEVEL0";
    private PlaytestWeaponConfiguration? FusionConfiguration => _fusion is null ? null : _fusionAssets?.Configuration(FusionTestActive);
    private bool FusionMovementStateBlocked => FusionTestActive || _fusion?.Selected.SourceGame is "rac2" or "rac3" ||
        _fusion?.LancerSession.ActiveShots.Count > 0;

    private static string FusionWeaponName(SourceWeaponKey key) => key.SourceGame == "rac1" ? Rac1WeaponProfiles.Name((Rac1WeaponId)key.NativeItemId) :
        key == FusionWeaponSession.Lancer ? "Lancer" : "Shock Cannon";

    private string? FusionWeaponProblem(SourceWeaponKey key) => _fusionAssets?.Problems.GetValueOrDefault(key) ??
        (key.SourceGame != "rac1" ? _fusionPreparation?.Status ?? _fusionAssetFailure : null);

    private string FusionWeaponDetail(SourceWeaponKey key) => FusionWeaponProblem(key) ??
        (_fusion is { } fusion ? $"{fusion.Status(key).Ammo}/{fusion.Status(key).Capacity} · {fusion.Status(key).State}" : "");

    private void ConfigureFusionWeapons(RuntimeWorld world)
    {
        if (_playerNative is not null) { _fusion = _playerNative.Weapons; return; }
        if (_playtest?.Reader is { Header.Weapons: null }) return;
        _fusionAcceptedHits = _fusionAudioStarted = _fusionModelAge = _fusionModelSequence = 0;
        _fusionLastDamage = null; _fusionDeathCleared = false;
        _fusionUseStarted = _fusionPoseTriggerHeld = false;
        _fusionDisplayedStatus = null;
        if (!_args.FusionTest || world.LevelId != 0)
        {
            _fusion = new(_args.FixedSeed ?? 1440, _rac1Weapons, _rac1HeldItem.Hand);
            return;
        }
        _fusionOriginalCampaign = _rac1CampaignSession;
        _fusionOriginalWallet = _rac1BoltWallet;
        _fusionOriginalGoldBolts = _rac1CollectedNovalisGoldBolts.ToArray();
        _rac1CollectedNovalisGoldBolts.Clear();
        _rac1CampaignSession = new(new Rac1CampaignState(), Rac1WeaponInventory.CreateOpeningLoadout());
        _rac1CampaignSession.StartLevelCheckpointSession(0, world.PlayerStart!);
        _rac1BoltWallet = new();
        _rac1WeaponPreviewInventory = null;
        _fusion = new(_args.FixedSeed ?? 1440, hand: _rac1HeldItem.Hand);
        GD.Print("[fusion] opt-in Veldin test: Bomb Glove [rac1/10] 10; Lancer [rac2/30] 40; Shock Cannon [rac3/43] 20. Original damage; campaign isolated.");
    }

    private void PrepareSharedWeaponSources(bool force = false)
    {
        RefreshPlayerSourceAvailability();
        var snapshot = _sources.Attached.Values.OrderBy(s => s.Game).ToArray();
        if (!force && _fusionSourceSnapshot is not null && snapshot.SequenceEqual(_fusionSourceSnapshot)) return;
        ResetSharedWeaponAssets();
        _fusionSourceSnapshot = snapshot;
        _fusionPreparation = new(snapshot, new FileVerificationCache(ProjectSettings.GlobalizePath("user://source-verification")));
        ShowSharedWeaponPreparation(_fusionPreparation.Status);
        GD.Print("[fusion] preparing shared trilogy weapon data in the background");
    }

    private void ShowSharedWeaponPreparation(string? status)
    {
        if (_args.CaptureFrame is not null || _args.ShotsPath is not null) return;
        if (status is null)
        {
            if (_fusionPreparationLayer is not null) _fusionPreparationLayer.Visible = false;
            return;
        }
        if (_fusionPreparationNotice is null)
        {
            _fusionPreparationLayer = new CanvasLayer { Layer = 190 };
            var panel = new PanelContainer
            {
                AnchorLeft = .5f,
                AnchorRight = .5f,
                AnchorTop = 1,
                AnchorBottom = 1,
                OffsetLeft = -360,
                OffsetRight = 360,
                OffsetTop = -66,
                OffsetBottom = -14,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _fusionPreparationNotice = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            panel.AddChild(_fusionPreparationNotice);
            _fusionPreparationLayer.AddChild(panel);
            AddChild(_fusionPreparationLayer);
        }
        _fusionPreparationLayer!.Visible = true;
        _fusionPreparationNotice.Text = "SHARED EQUIPMENT  ·  " + status;
    }

    private void TickFusionPreparation()
    {
        if (_fusionPreparation is not { } preparation) return;
        if (!preparation.Completion.IsCompleted)
        {
            if (_fusionLoadingStatus != preparation.Status)
            {
                _fusionLoadingStatus = preparation.Status;
                ShowSharedWeaponPreparation(_fusionLoadingStatus);
                RefreshRac1HudState();
            }
            return;
        }
        _fusionPreparation = null;
        try
        {
            // Only texture uploads and scene attachment run on the main thread.
            var data = preparation.Completion.GetAwaiter().GetResult();
            _fusionAssets = new(data);
            if (_playerAvatarView is not null) AttachFusionWeaponViews(_playerAvatarView);
            foreach (var problem in _fusionAssets.Problems.Values) GD.Print("[fusion] " + problem);
            GD.Print($"[fusion] shared weapon preparation complete; reused {data.ReusedVerifications}/3 saved verifications; retained for this source session");
        }
        catch (Exception error)
        {
            _fusionAssets?.Dispose(); _fusionAssets = null;
            _fusionAssetFailure = "Weapon preparation failed. Reattach the source in Game Sources.";
            GD.PrintErr("[fusion] weapon preparation failed: " + error);
        }
        finally { preparation.Dispose(); }
        _fusionLoadingStatus = null;
        ShowSharedWeaponPreparation(_fusionAssetFailure);
        RefreshPlayerEquipmentAvailability();
        RefreshRac1HudState();
    }

    private void ResetSharedWeaponAssets()
    {
        // Source changes and application exit own this lifetime, not level teardown.
        ClearFusionTransients();
        ReleaseFusionWeaponViews();
        _fusionPreparation?.Dispose(); _fusionPreparation = null;
        _fusionLoadingStatus = _fusionAssetFailure = null;
        _fusionSourceSnapshot = null;
        _fusionAssets?.Dispose(); _fusionAssets = null;
        ShowSharedWeaponPreparation(null);
    }

    private void SyncFusionNativeSelection()
    {
        if (_fusion is null) return;
        bool foreign = _fusion.Selected.SourceGame != "rac1";
        _fusion.BindRac1Inventory(_rac1Weapons);
        _fusion.Select(new("rac1", _rac1Weapons.CurrentItemId));
        if (foreign)
        {
            StopFusionAudio();
            _fusionUseStarted = _fusionPoseTriggerHeld = false;
            if (_fusionBeam is not null) _fusionBeam.Visible = false;
        }
    }

    private void ToggleFusionTest()
    {
        if (_playtest is not null) { ReportMovementState("Choose the fusion loadout before starting an input recording."); return; }
        if (OrdinaryPlayerSession) { ReportMovementState("Launch with --fusion-test to use the isolated native Veldin fixture loadout."); return; }
        if (_activeDestination is not { DestinationId: "rac1:LEVEL0" } destination) return;
        bool enable = !FusionTestActive;
        FlushRac1CampaignSaves("before fusion test");
        _args = _args with { FusionTest = enable };
        ClosePauseMenu();
        OnDestinationChosen(destination);
    }

    private void SelectWeaponKey(SourceWeaponKey key)
    {
        if (_playerNative is not null)
        {
            var entry = ReadPlayerEquipment().SingleOrDefault(entry => entry.Definition.Item == key);
            if (entry?.Selectable != true) { ReportMovementState(entry?.Reason ?? "No implemented equipment registration."); return; }
        }
        if (_fusion is null || key.SourceGame == "rac1" && !FusionTestActive)
        {
            if (key.SourceGame == "rac1") OnRac1WeaponSelectionRequested((Rac1WeaponId)key.NativeItemId);
            RefreshPlayerEquipmentAvailability();
            return;
        }
        var previous = _fusion.Selected;
        if (!_fusion.Select(key)) return;
        if (previous != key)
        {
            _previousSelectedWeapon = previous;
            StopFusionAudio();
            _fusionModelSequence = _fusionModelAge = 0;
            _fusionUseStarted = _fusionPoseTriggerHeld = false;
            _rac1BombFireRequested = _rac1AdditionalFireRequested = _rac1EquipmentUsePressed = false;
            _rac1BombThrowActive = false;
            if (_fusionBeam is not null) _fusionBeam.Visible = false;
        }
        if (key.SourceGame == "rac1") HoldSelectedRac1HandItem();
        RefreshPlayerEquipmentAvailability();
        RefreshRac1WrenchPresentationVisibility();
        RefreshRac1HudState();
        GD.Print($"[fusion] equipped {key}: {FusionWeaponDetail(key)}");
    }

    private HudPresentationState FusionHud(HudPresentationState state)
    {
        if (_playerNative is not null)
        {
            var entry = ReadPlayerEquipment().Single(entry => entry.Definition.Item == _playerNative.Weapons.Selected);
            return state with
            {
                CurrentWeapon = new(entry.Definition.Item!.Value.ToString(), entry.Definition.DisplayName,
                    entry.State.Capacity is { } capacity ? new HudAmmo(entry.State.Ammo!.Value, capacity) : null)
                {
                    Status = entry.Reason ?? (_playerNative.Held.Hand.WrenchHeld && entry.Definition.Item != PlayerEquipmentRegistrations.Wrench
                        ? "Wrench held · use to draw" : _playerNative.Weapons.Status(entry.Definition.Item.Value).State),
                },
            };
        }
        if (_fusion is null || !FusionTestActive && _fusion.Selected.SourceGame == "rac1") return state;
        var status = _fusion.Status(_fusion.Selected);
        return state with
        {
            CurrentWeapon = new(status.Weapon.ToString(),
            $"{FusionWeaponName(status.Weapon)} [{status.Weapon.SourceGame}]", new(status.Ammo, status.Capacity))
            {
                Status = FusionWeaponProblem(status.Weapon) ?? (_fusion.Hand.WrenchHeld && _fusion.Selected.SourceGame != "rac1"
                ? "Wrench held · use to draw" : $"{(FusionTestActive ? "Fusion test · " : "")}{status.State}")
            }
        };
    }

    private void AttachFusionWeaponViews(Node3D avatar)
    {
        if (_fusion is null || _fusionAssets is null || _fusionHeldViews.Count != 0) return;
        foreach (var key in new[] { FusionWeaponSession.Lancer, FusionWeaponSession.ShockCannon })
        {
            if (!_fusionAssets.Available(key)) continue;
            var view = new FusionMeshView { Name = key.SourceGame + "_original_weapon", Visible = false };
            avatar.AddChild(view); _fusionHeldViews.Add(key, view);
        }
        _fusionBeam = new() { Name = "rac3_original_beam", Visible = false };
        _sceneResult!.Root.AddChild(_fusionBeam);
    }

    private void StepFusionWeapons()
    {
        if (_fusion is null || _fusionAssets is null || _player is null || _movementStateLoading) return;
        bool alive = PlayerAlive;
        if (!alive) { ClearFusionOnDeath(); return; }
        _fusionDeathCleared = false;
        Vector3 origin = FusionMuzzle();
        Vector3 aim = FusionAim(origin) - origin;
        bool admitted = !_player.Scripted && !_player.Rac1WrenchFrame.Active && !_rac1WrenchThrow.Active &&
            !_player.Rac1WaterHandItemHidden && !QuickSelectOpen && _fusionAssets.Available(_fusion.Selected);
        bool wasWindup = _fusion.CannonFrame?.State == UyaShockCannonState.Windup;
        _fusionPoseTriggerHeld = _player.Rac1EquippedItemHeld && admitted && _fusion.Hand.IsHolding(_fusion.Selected);
        var frame = _fusion.Advance(new(_fusionPoseTriggerHeld, admitted, ToNumerics(origin), ToNumerics(aim)));
        _fusionUseStarted = frame.Effects.Any(e => e.Kind == "shot") ||
            !wasWindup && _fusion.CannonFrame?.State == UyaShockCannonState.Windup;
        var contacts = new List<WeaponContact>();
        foreach (var query in frame.Queries) contacts.AddRange(TraceFusionQuery(query, origin));
        foreach (var intent in _fusion.ResolveContacts(frame, contacts))
        {
            var target = EquipmentTargets.FirstOrDefault(n => n.Source.NativeClassId == intent.Target.NativeClassId && n.Source.InstanceIndex == intent.Target.InstanceIndex);
            if (target is not null && FusionDamagePolicy.Translate(intent) is { } damage && ApplyRac1AdditionalDamage(target, damage))
            { _fusionAcceptedHits++; _fusionLastDamage = intent; }
        }
        foreach (var cue in frame.Audio) QueueFusionAudio(cue);
        TickFusionAudio();
        foreach (var effect in frame.Effects.Where(e => e.Kind == "shot"))
        { _fusionModelSequence = 2; _fusionModelAge = 0; }
        SynchronizeFusionProjectiles(frame);
        if (frame.Queries.Any(q => q.Kind == WeaponQueryKind.Ray)) SpawnFusionSpread(frame, contacts);
        for (int i = _fusionEffects.Count - 1; i >= 0; i--)
        {
            var item = _fusionEffects[i];
            if (--item.Remaining <= 0) { item.Node.QueueFree(); _fusionEffects.RemoveAt(i); }
            else _fusionEffects[i] = item;
        }
        var status = _fusion.Status(_fusion.Selected);
        if (status != _fusionDisplayedStatus || _fusion.Hand.Equipment != _fusionDisplayedHand)
        {
            RefreshRac1HudState(); _fusionDisplayedStatus = status; _fusionDisplayedHand = _fusion.Hand.Equipment;
        }
    }

    private Vector3 FusionMuzzle()
    {
        Vector3 muzzle = _player!.GlobalPosition + Vector3.Up * .9f + EquipmentForward * .5f;
        if (_fusion is not null && _fusionHeldViews.TryGetValue(_fusion.Selected, out var view))
        {
            var muzzlePoint = FusionWeaponMount.Muzzle(_fusion.Selected);
            muzzle = view.ToGlobal(new Vector3(muzzlePoint.X, muzzlePoint.Y, muzzlePoint.Z));
        }
        var ray = PhysicsRayQueryParameters3D.Create(_player!.GlobalPosition + Vector3.Up * .8f, muzzle);
        ray.Exclude = [_player.GetRid()];
        var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(ray);
        return hit.TryGetValue("position", out var point) ? point.AsVector3() - EquipmentForward * .03f : muzzle;
    }

    private Vector3 FusionAim(Vector3 origin)
    {
        float range = _fusion!.Selected == FusionWeaponSession.Lancer ? GcLancerProfile.ProjectileRange : UyaShockCannonProfile.RayLength;
        var candidates = EquipmentGeometry.Where(e => FusionDamagePolicy.Admits(new("rac1", e.Node.Source.NativeClassId, e.Node.Source.InstanceIndex)) &&
            (_fusion.Selected != FusionWeaponSession.Lancer || e.Node.Source.NativeClassId != 749) && IsRac1GameplayActive(e.Node))
            .Select(e => e.Bounds.GetCenter()).Where(p => p.DistanceSquaredTo(origin) is > .1f &&
                p.DistanceSquaredTo(origin) < range * range && (p - origin).Normalized().Dot(EquipmentForward) > .9f && BombHasLineOfSight(origin, p))
            .OrderBy(p => p.DistanceSquaredTo(origin));
        return candidates.FirstOrDefault(origin + EquipmentForward * range);
    }

    private IEnumerable<WeaponContact> TraceFusionQuery(WeaponQuery query, Vector3 muzzle)
    {
        Vector3 from = ToGodot(query.From), to = ToGodot(query.To);
        var candidates = EquipmentGeometry.Where(e => FusionDamagePolicy.Admits(new("rac1", e.Node.Source.NativeClassId, e.Node.Source.InstanceIndex)) &&
            IsRac1GameplayActive(e.Node) && (!_rac1Enemies.TryGetValue(e.Node.Source.InstanceIndex, out var enemy) || !enemy.RewardEmitted))
            .OrderBy(e => e.Node.Source.NativeClassId).ThenBy(e => e.Node.Source.InstanceIndex);
        if (query.Kind == WeaponQueryKind.Sphere)
        {
            foreach (var (node, bounds) in candidates)
            {
                Vector3 nearest = new(Mathf.Clamp(from.X, bounds.Position.X, bounds.End.X), Mathf.Clamp(from.Y, bounds.Position.Y, bounds.End.Y), Mathf.Clamp(from.Z, bounds.Position.Z, bounds.End.Z));
                if (nearest.DistanceSquaredTo(from) > query.Radius * query.Radius || !BombHasLineOfSight(muzzle, nearest)) continue;
                yield return new(query.Index, new("rac1", node.Source.NativeClassId, node.Source.InstanceIndex), 0, muzzle.DistanceTo(nearest));
            }
            yield break;
        }
        var ray = PhysicsRayQueryParameters3D.Create(from, to); ray.Exclude = Rac1DamageableRayExclusions(); ray.HitFromInside = true;
        var hit = _player!.GetWorld3D().DirectSpaceState.IntersectRay(ray);
        float length = from.DistanceTo(to);
        float closest = hit.TryGetValue("position", out var point) ? from.DistanceTo(point.AsVector3()) / length : float.PositiveInfinity;
        WeaponTargetKey? target = null;
        foreach (var (node, bounds) in candidates)
        {
            if (!SegmentBoxFraction(from, to, bounds.Grow(query.Radius), out float fraction) || fraction > closest + .00001f) continue;
            closest = fraction; target = new("rac1", node.Source.NativeClassId, node.Source.InstanceIndex);
        }
        if (float.IsFinite(closest)) yield return new(query.Index, target, Mathf.Clamp(closest, 0, 1), length * closest);
    }

    private void SynchronizeFusionProjectiles(WeaponFrame frame)
    {
        var active = _fusion!.LancerSession.ActiveShots.Select(s => new WeaponShotKey(FusionWeaponSession.Lancer, s.Generation, s.Sequence)).ToHashSet();
        foreach (var (key, node) in _fusionProjectiles.ToArray())
            if (!active.Contains(key)) { node.QueueFree(); _fusionProjectiles.Remove(key); }
        foreach (var query in frame.Queries.Where(q => q.Shot.Weapon == FusionWeaponSession.Lancer && active.Contains(q.Shot)))
        {
            if (!_fusionProjectiles.TryGetValue(query.Shot, out var node))
            {
                node = new() { Name = "rac2_original_lancer_projectile" };
                _sceneResult!.Root.AddChild(node); node.Sample(_fusionAssets!.Projectile, _fusionAssets); _fusionProjectiles.Add(query.Shot, node);
            }
            node.GlobalPosition = ToGodot(query.To);
        }
    }

    private void SpawnFusionSpread(WeaponFrame frame, IReadOnlyList<WeaponContact> contacts)
    {
        // Bounded OBP feedback for the not-yet-decoded native spread renderer.
        // It uses no substituted R&C1 effect texture/model.
        using var mesh = new ImmediateMesh(); mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        foreach (var query in frame.Queries.Where(q => q.Kind == WeaponQueryKind.Ray))
        {
            float fraction = contacts.Where(c => c.QueryIndex == query.Index).Select(c => c.Fraction).DefaultIfEmpty(1).Min();
            mesh.SurfaceAddVertex(ToGodot(query.From)); mesh.SurfaceAddVertex(ToGodot(NVector3.Lerp(query.From, query.To, fraction)));
        }
        mesh.SurfaceEnd();
        using var material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new(.35f, .75f, 1) };
        var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = material };
        _sceneResult!.Root.AddChild(node); _fusionEffects.Add((node, 5));
    }

    private void StepFusionWeaponViews(Rac1PlayerAnimationPresentationController controller)
    {
        if (_fusion is null || _fusionAssets is null || _playerAvatar is null) return;
        _fusionModelAge++;
        foreach (var (key, view) in _fusionHeldViews)
        {
            view.Visible = key == _fusion.Selected && _fusion.Hand.IsHolding(key) && _fusionAssets.Available(key) && PlayerAlive &&
                _player?.Rac1WrenchFrame.Active != true && !_rac1WrenchThrow.Active && _player?.Rac1WaterHandItemHidden != true;
            if (!view.Visible) continue;
            // Native hand-matrix slot0 (semantic0/joint56), after the source
            // arm layers. Both retail consumers copy this basis and origin.
            var hand = controller.CurrentAttachmentPose(PlayerEquipmentPose.HandAttachmentKey);
            if (hand is not null)
            {
                Vector3 Point(double x, double y, double z) => PlayerAvatarView.ToGodotLocal(hand.Transform(new(x, y, z)), _playerAvatar.Axes, _playerAvatar.BaseHeight, true);
                Vector3 origin = Point(0, 0, 0);
                // Convert the held mesh's reflected X / swapped YZ axes back
                // through the avatar's native coordinate frame exactly once.
                var basis = new Basis(Point(-1, 0, 0) - origin, Point(0, 0, 1) - origin, Point(0, 1, 0) - origin);
                view.Transform = new(basis.Orthonormalized(), origin);
            }
            if (key == FusionWeaponSession.Lancer && _fusionAssets.Lancer is { } lancer)
            {
                var clip = lancer.Model.Sequences.Single(s => s.Index == _fusionModelSequence);
                int frame = _fusionModelAge / 2;
                if (frame >= clip.Frames.Count) { _fusionModelSequence = 1; frame = 0; }
                view.Sample(GcLancerPresentation.HeldModel(lancer, _fusionModelSequence, frame), _fusionAssets);
            }
            else if (_fusionAssets.Cannon is { } cannon)
            {
                // Source clip selection is bounded test presentation; native
                // player-to-held animation selection remains unrecovered.
                int sequence = _fusion.CannonFrame?.ChargeUpdates > 0 ? 2 : 0;
                int frame = (_fusionModelAge / 2) % cannon.Sequences.Single(s => s.Index == sequence).Frames.Count;
                view.Sample(UyaShockCannonAssets.Model(cannon, sequence, frame), _fusionAssets);
            }
        }
        if (_fusionBeam is not null)
        {
            _fusionBeam.Visible = _fusion.CannonFrame?.Effects.Any(e => e.Kind == UyaShockCannonEffectKind.Beam) == true;
            if (_fusionBeam.Visible)
            {
                _fusionBeamScroll = _fusionBeamScroll.Advance();
                // Convert host Y-up back through the source Z-up beam projector.
                Vector3 muzzle = FusionMuzzle();
                var basis = UyaShockCannonBeam.Basis.FromNativeAim(ToEffectNative(EquipmentForward), ToEffectNative(muzzle));
                _fusionBeam.Sample(UyaShockCannonBeam.Build(_fusion.CannonFrame!, basis, _fusionBeamScroll).Select(l => l.Mesh).ToArray(), _fusionAssets);
            }
        }
    }

    private void QueueFusionAudio(WeaponAudioIntent cue)
    {
        if (cue.Operation == WeaponAudioOperation.StopLoop) { StopFusionAudio(cue.LocalSound); return; }
        int owner = cue.Operation == WeaponAudioOperation.StartLoop ? cue.LocalSound : -1;
        foreach (var voice in _fusionAssets!.Audio(cue)) _fusionPendingAudio.Add((_fusion!.Tick + (int)Math.Ceiling(voice.Delay * 60), owner, voice));
    }

    private void TickFusionAudio()
    {
        foreach (var pending in _fusionPendingAudio.Where(p => p.Due <= _fusion!.Tick).ToArray())
        {
            _fusionPendingAudio.Remove(pending);
            var voice = pending.Voice;
            if (!RuntimeAudioHost.TryPrepare(new(voice.Clip, RuntimeAudioCategory.SoundEffect, voice.Loop, voice.Gain), out var clip, out var problem))
                throw new InvalidDataException("Fusion audio unavailable: " + problem);
            using var stream = new AudioStreamWav
            {
                Format = AudioStreamWav.FormatEnum.Format16Bits,
                Data = clip!.Pcm16LittleEndian,
                MixRate = clip.SampleRateHz,
                LoopMode = voice.Loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
                LoopBegin = clip.LoopStartFrame,
                LoopEnd = clip.LoopEndFrame ?? 0
            };
            var player = new AudioStreamPlayer { Stream = stream, VolumeDb = clip.VolumeDb }; AddChild(player);
            var owned = (pending.Owner, player); _fusionAudioPlayers.Add(owned);
            player.Finished += () => { _fusionAudioPlayers.Remove(owned); player.QueueFree(); };
            player.Play(); _fusionAudioStarted++;
        }
    }

    private void StopFusionAudio(int? owner = null, bool freeImmediately = false)
    {
        _fusionPendingAudio.RemoveAll(p => owner is null || p.Owner == owner);
        foreach (var owned in _fusionAudioPlayers.Where(p => owner is null || p.Owner == owner).ToArray())
        {
            if (IsInstanceValid(owned.Player)) { owned.Player.Stop(); if (freeImmediately) owned.Player.Free(); else owned.Player.QueueFree(); }
            _fusionAudioPlayers.Remove(owned);
        }
    }

    private void ClearFusionOnDeath()
    {
        if (_fusionDeathCleared) return;
        ClearFusionTransients(); _fusionDeathCleared = true;
    }

    private void ClearFusionTransients(bool freeImmediately = false)
    {
        StopFusionAudio(freeImmediately: freeImmediately);
        if (_fusionAssets is not null)
        {
            foreach (var (id, projectile) in _rac1Projectiles.ToArray()) RetireBomb(id, projectile);
            ResetRac1BombPresentation();
            _rac1BombGlove = new(_rac1Weapons);
            _rac1BombFireRequested = _rac1EquipmentUsePressed = false;
        }
        _fusion?.ClearTransientState();
        foreach (var node in _fusionProjectiles.Values.Cast<Node3D>().Concat(_fusionEffects.Select(e => e.Node)))
            if (IsInstanceValid(node)) { if (freeImmediately) node.Free(); else node.QueueFree(); }
        _fusionProjectiles.Clear(); _fusionEffects.Clear();
        _fusionUseStarted = _fusionPoseTriggerHeld = false;
        if (_fusionBeam is not null && IsInstanceValid(_fusionBeam)) _fusionBeam.Visible = false;
        _fusionBeamScroll = default;
    }

    private void ResetFusionWeapons(bool freeImmediately = false)
    {
        ClearFusionTransients(freeImmediately);
        ReleaseFusionWeaponViews(freeImmediately);
        _fusion = null;
        if (_fusionOriginalCampaign is { } campaign)
        {
            _rac1CampaignSession = campaign; _rac1BoltWallet = _fusionOriginalWallet!;
            _rac1CollectedNovalisGoldBolts.Clear(); _rac1CollectedNovalisGoldBolts.UnionWith(_fusionOriginalGoldBolts);
        }
        _fusionOriginalCampaign = null; _fusionOriginalWallet = null; _fusionOriginalGoldBolts = [];
    }

    private void ReleaseFusionWeaponViews(bool freeImmediately = false)
    {
        foreach (var view in _fusionHeldViews.Values)
            if (IsInstanceValid(view)) { if (freeImmediately) view.Free(); else view.QueueFree(); }
        _fusionHeldViews.Clear();
        if (_fusionBeam is not null && IsInstanceValid(_fusionBeam)) { if (freeImmediately) _fusionBeam.Free(); else _fusionBeam.QueueFree(); }
        _fusionBeam = null;
    }

    private static NVector3 ToNumerics(Vector3 value) => new(value.X, value.Y, value.Z);
    private static Vector3 ToGodot(NVector3 value) => new(value.X, value.Y, value.Z);
}
