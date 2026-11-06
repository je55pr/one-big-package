using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OBP.Game;

/// <summary>
/// Binds the active R&amp;C1 world lease's modules to the application's owned
/// components. Every port is a typed, narrow view the module consumes; no native
/// formula, tick or damage dispatch is implemented here. A new R&amp;C1 mechanic adds
/// its ports to its own module and one binding below, never to <c>OBPGame</c>.
/// </summary>
internal sealed class Rac1WorldComposition(ApplicationComponents components) : CompositionBase(components)
{
    private void ShowRac1CombatRetry() => _worldScenes?.Current?.Resources.Rac1Gameplay?.Lifecycle.ShowCombatRetry();
    private void TickRac1VeldinEnvironmentalDeath() => _worldScenes?.Current?.Resources.Rac1Gameplay?.Lifecycle.TickEnvironmentalDeath();
    private void PersistRac1CampaignState(string reason) => _rac1Campaign.Persistence.Persist(reason);
    private void AddChild(Node node) => _host.AddChild(node);
    private ulong _lastQuickSelectTap { set { if (value == 0) Modals.ResetWheelGesture(); } }
    private SourceWeaponKey? _previousSelectedWeapon { get => Modals.PreviousSelectedWeapon; set => Modals.PreviousSelectedWeapon = value; }
    private bool InWorldMode => Context.Mode().World;

    internal Rac1WorldModule CreateRac1WorldModule(RuntimeWorld world, RuntimeWorldScene.Result scene) => new(world, scene, _persistentPlayer.Rac1EffectRandom(_args.FixedSeed),
        new Rac1LootPorts(() => _player, () => _world, () => _rac1Weapons, () => _rac1Nanotech,
            () => _rac1BoltWallet, () => _persistentPlayer.Rac1EffectRandom(_args.FixedSeed), () => (_worldScenes?.Current?.Resources.Rac1Gameplay?.Wrench.ThrowActive == true),
            () => Rac1World.Wrench.WorldPosition, () => OrdinaryPlayerSession, () => _equipmentPresentation,
            () => Sources.Setup.Library.Get(ObpSourceGame.Rac1)?.Path, _worldHost.PlayEffect,
            () => _playerAudio?.RetireWorld(), PersistRac1CampaignState,
            () => RefreshRac1HudState(), value => RefreshRac1HudState(Rac1HudProjection.CollectedBoltFeedback(value)),
            (nativeClass, localSound) => _equipmentPresentation.WeaponAudio.Record(nativeClass, localSound, (_worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.WorldTick ?? 0), _args.Rac1WeaponsSmoke), () => _rac1Campaign.CollectedNovalisGoldBolts),
        new Rac1BreakablePorts(() => _player,
            owner => ReferenceEquals(_worldScenes?.Current?.Resources.Rac1Gameplay, owner),
            (min, max) => _persistentPlayer.Rac1EffectRandom(_args.FixedSeed).Inclusive(min, max),
            centre => _persistentPlayer.SelectRac1CrateReward(centre, _args.FixedSeed),
            (from, to) => Rac1World.Weapons.Contacts.HasLineOfSight(from, to),
            damage => Rac1World.Enemies.ApplyRac1EnemyPlayerDamage(damage),
            (node, admission) => Rac1World.WrenchDamage.CompleteCrateDamage(node, admission)),
        new Rac1WorldResetPorts(
            Player: () =>
            {
                FusionWeapons.ResetWorld();
                if (_playerNative is null) _rac1HeldItem.ResetForLevelEntry();
                else _playerNative.EndWorld();
            },
            Wheel: () => { _lastQuickSelectTap = 0; _previousSelectedWeapon = null; },
            Pickups: () => Rac1World.Loot.ResetRac1PickupPresentation(),
            GoldBolts: () => Rac1World.Loot.ResetRac1GoldBolts(),
            AdditionalWeapons: () => Rac1World.UsePresentation.ResetAdditional(),

            BombPresentation: () => Rac1WeaponEffectsHost.ResetOrFallback(_worldScenes?.Current?.Resources.Rac1Gameplay, _equipmentPresentation)),
        new Rac1WorldActorPorts(world => Rac1World.Loot.ConfigureRac1PickupPresentation(world), node => Rac1World.Loot.AddRac1CrateCollider(node),
            checkpoints => checkpoints.Configure(() => Sources.Setup.Library.Get(ObpSourceGame.Rac1)?.Path),
            (world, result) => Rac1World.Loot.ConfigureRac1PlacedBolts(world, result),
            (world, result) => Rac1World.Loot.ConfigureRac1GoldBolts(world, result)),
        new Rac1WorldTickPorts(() => MovementReproduction.IsLoading,
            checkpoints => checkpoints.Tick(_player, _rac1Campaign.Session.LevelCheckpoint),
            delta => Rac1World.Loot.TickRac1Pickups(delta), () => Rac1World.Loot.TickRac1GoldBolts()),
        new Rac1MissionWorldPorts(
            SourcePath: () => Sources.Setup.Library.Get(ObpSourceGame.Rac1)?.Path,
            Campaign: () => _rac1Campaign, Wallet: () => _rac1BoltWallet,
            Health: () => _rac1Nanotech, Player: () => _player,
            QuickSelectOpen: () => QuickSelectOpen,
            Paused: () => GetTree().Paused, Pause: paused => GetTree().Paused = paused,
            AttachPrompt: prompt => _ui.AddChild(prompt),
            AttachScene: sceneUi => AddChild(sceneUi),
            Persist: PersistRac1CampaignState, RefreshHud: () => RefreshRac1HudState(),
            Navigate: destination => OnDestinationChosen(destination),
            WorldStillCurrent: () => ReferenceEquals(_world, world)),
        new Rac1LifecycleWorldPorts(
            Ordinary: () => OrdinaryPlayerSession,
            Player: () => _player, Health: () => _rac1Nanotech,
            Campaign: () => _rac1Campaign,
            ActiveDestination: () => _activeDestination,
            Navigate: destination => OnDestinationChosen(destination),
            ClearFusionTransients: () => FusionWeapons.ClearTransients(),
            ResetHeldItem: _rac1HeldItem.ResetForLevelEntry,
            RefreshWrenchPresentation: () => _equipmentPresentation.RefreshRac1HandVisibility(Rac1HandPresentation, _avatarPresentation),
            RefreshHud: () => RefreshRac1HudState(),
            ResetPlayerRetry: () => PlayerLife.ResetRetry(),
            AttachRetry: panel => _ui.AddChild(panel),
            WindowUnfocusable: () => GetWindow().Unfocusable,
            WorldStillCurrent: () => ReferenceEquals(_world, world)),

        new Rac1PlayerGameplayPorts(() => OrdinaryPlayerSession, () => _playerNative is not null, () => PlayerLife.Alive,
            () => _rac1Nanotech, () => _rac1Weapons, _persistentPlayer.Equipment.RefreshMovementEquipment,
            Rac1CombatInput.PrimaryAttack, Rac1CombatInput.Select, Rac1CombatInput.Cycle,
            () => Rac1World.UsePresentation.TogglePreview(), frame => _worldScenes?.Current?.Resources.Rac1Gameplay?.Wrench.OnPhysicsUpdated(frame), FusionWeapons.Configure, () => FusionWeapons.Session is not null,
            FusionWeapons.ConfigureNativePresentation, _persistentPlayer.BeginParityWorld,
            () => TryCompleteRac1EnvironmentalRestart(automatic: true), TickRac1VeldinEnvironmentalDeath,
            () => { FusionWeapons.ClearOnDeath(); Rac1WrenchHost.ResetOrFallback(_worldScenes?.Current?.Resources.Rac1Gameplay?.Wrench, _playerAudio, _player); ShowRac1CombatRetry(); }),
        new Rac1WorldContactPorts(() => GodotPlayerLifecycleHost.CompleteNativeWaterAction(
            _worldScenes?.Current?.Resources.Rac1Gameplay?.Mechanisms.HasWater == true, _player, _rac1Nanotech,
            status => Rac1World.Context.CombatStatus = status, () => RefreshRac1HudState()), sound => _playerAudio?.Play(new(0, sound))),
        new Rac1AuthoredMechanismPorts(
            () => Sources.Setup.Library.Get(ObpSourceGame.Rac1)?.Path,
            (nativeClass, sound, position) => Rac1World.Loot.PlayRac1PickupSound(nativeClass, sound, position),
            intent => _worldHost.StartLevelAudio([intent]),
            _worldHost.StopLevelAudio,
            status => Rac1World.Context.CombatStatus = status),
        new Rac1GameplayHudPorts(_hudState, () => OrdinaryPlayerSession, () => _playerSession is not null,
            PlayerLife.PublishHud, FusionWeapons.Hud, () => _rac1Nanotech, () => _rac1Weapons, () => _rac1BoltWallet,
            () => _persistentPlayer.Rac1Use.Session?.Cargo.Count ?? 0, () => _player, () => Rac1World.Weapons.Detector.HudText()),
        new Rac1MetalDetectorPorts(
            () => Sources.Setup.Library.Get(ObpSourceGame.Rac1)?.Path,
            () => _rac1Campaign.Session.BuriedBolts, () => _persistentPlayer.WeaponPreview is not null,
            () => OrdinaryPlayerSession,
            () => ReadPlayerEquipment().Single(entry => entry.Definition.Id == "rac1/item/27").Reason
                ?? "The common Metal Detector action is unavailable.",
            () => _rac1Weapons.OwnsNativeItem(Rac1MetalDetectorSession.NativeGadgetItem),
            () => _player, () => _rac1Nanotech.Probe().IsDead, () => Rac1WeaponUsePresentationHost.EquipmentForward(_player),
            MovementReproduction.Report, status => Rac1World.Context.CombatStatus = status, () => RefreshRac1HudState(),
            (position, bursts) => Rac1World.Loot.SpawnRac1BoltPickups(position,
                Enumerable.Range(0, bursts).SelectMany(_ => Rac1World.Context.BoltPickups.Emit(Rac1MetalDetectorSession.BoltsPerBurst)).ToArray()),
            PersistRac1CampaignState),
        new Rac1WeaponTargetPorts(
            () => Rac1World.Enemies.Targets.Concat(Rac1World.Presentation.CrateNodes),
            () => Rac1World.Enemies.Frogs.Rac1QueenDamageTargets,
            id => Rac1World.Weapons.Morph.ViewFor(id),
            node => Rac1World.Enemies.IsRac1GameplayActive(node),
            node => Rac1World.Enemies.Frogs.IsQueenNode(node),
            () => Rac1World.Enemies.Frogs.QueenDamageAdmitted,
            id => Rac1World.Enemies.Roster.TryGetValue(id, out var enemy) && enemy.RewardEmitted,
            () => Rac1World.Enemies.Targets,
            id => Rac1World.Weapons.Morph.Contains(id)),
        new Rac1WeaponContactPhysicsPorts(
            () => _player, () => Rac1WeaponUsePresentationHost.EquipmentForward(_player),
            () => Rac1World.Loot.CrateBodies.Values.Concat<CollisionObject3D>(Rac1World.Enemies.Roster.Values.Select(enemy => enemy.Body)),
            ActorHost.AddOcclusionExclusions),
        new Rac1SuckCannonPorts(
            () => _persistentPlayer.Rac1Use.Session, () => _player is not null,
            () => Rac1WeaponUsePresentationHost.EquipmentMuzzle(Rac1WeaponId.SuckCannon, 0, _equipmentPresentation, _avatarPresentation, _player), () => Rac1WeaponUsePresentationHost.EquipmentForward(_player),
            () => (_worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.WorldTick ?? 0),
            (min, max) => _persistentPlayer.Rac1EffectRandom(_args.FixedSeed).Signed(min, max),
            () => Rac1World.Weapons.Contacts.Targets,
            () => Rac1World.Enemies.Targets.Concat(Rac1World.Presentation.CrateNodes),
            id => Rac1World.Weapons.Morph.ViewFor(id),
            (from, to) => Rac1World.Weapons.Contacts.HasLineOfSight(from, to),
            (type, tint, scale) => _equipmentPresentation.Explosions.CreateEquipmentParticle(type, tint, scale,
                _equipmentPresentation.NativeViews.Assets, _avatarPresentation.GloveAsset?.ParticleTexturesByPoolType),
            (mote, position, step) =>
            {
                _sceneResult!.Root.AddChild(mote); mote.GlobalPosition = position;
                Rac1World.Weapons.Projectiles.AddMote(mote, step);
            },
            (target, point) => Rac1World.RetireCapturedTarget(target),
            () => _sceneResult!.Root, nativeClass => Rac1World.UsePresentation.CreateEquipmentWorldView(nativeClass),
            (origin, distance, cone) => Rac1World.Weapons.Contacts.Aim(origin, distance, cone),
            (shot, visual, origin, direction) =>
            {
                Rac1World.Weapons.Projectiles.AddSuckCarrier(shot.Generation, visual, origin,
                    direction * Rac1WeaponReconstructionPolicy.SuckProjectileStep);
                Rac1World.Effects.SpawnEquipmentFlash(origin, 27, new(.5f, .7f, 1, 1), .6f, 7);
            },
            (cls, sound, position) => Rac1World.Loot.PlayRac1PickupSound(cls, sound, position),
            () => RefreshRac1HudState()),
        new Rac1ArsenalMissilePorts(
            (weapon, barrel) => Rac1WeaponUsePresentationHost.EquipmentMuzzle(weapon, barrel, _equipmentPresentation, _avatarPresentation, _player),
            () => Rac1WeaponUsePresentationHost.EquipmentForward(_player), nativeClass => Rac1World.UsePresentation.CreateEquipmentWorldView(nativeClass),
            (origin, range, cone, excluded) => Rac1World.Weapons.Contacts.FindArsenalTarget(origin, range, cone, excluded),
            node => Rac1World.Weapons.Contacts.IsAimTarget(node),
            (from, to, radius) => Rac1World.Weapons.Contacts.Contact(from, to, radius),
            () => _player, () => PlayerLife.Alive, () => _rac1Weapons.Equipped,
            () => (_worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.WorldTick ?? 0), (origin, type, color, size, ticks) => Rac1World.Effects.SpawnEquipmentFlash(origin, type, color, size, ticks), (weapon, position, radius, damage, flags) => Rac1World.WeaponDamage.ApplyBlast(weapon, position, radius, damage, flags),
            (weapon, origin) => Rac1World.Effects.SpawnWeaponExplosion(weapon, origin), (cls, sound, position) => FusionWeapons.PlayNativeSound(cls, sound, position),
            () => _sceneResult!.Root),
        new Rac1ArsenalDeployablePorts(
            nativeClass => Rac1World.UsePresentation.CreateEquipmentWorldView(nativeClass), weapon => Rac1WeaponUsePresentationHost.EquipmentMuzzle(weapon, 0, _equipmentPresentation, _avatarPresentation, _player),
            () => Rac1WeaponUsePresentationHost.EquipmentForward(_player), () => _player, () => (_worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.WorldTick ?? 0),
            (origin, range, cone) => Rac1World.Weapons.Contacts.FindArsenalTarget(origin, range, cone),
            node => Rac1World.Weapons.Contacts.IsAimTarget(node),
            (from, to, radius) => Rac1World.Weapons.Contacts.Contact(from, to, radius),
            (from, to) => Rac1World.Weapons.Contacts.HasLineOfSight(from, to),
            (origin, type, color, size, ticks) => Rac1World.Effects.SpawnEquipmentFlash(origin, type, color, size, ticks), (weapon, position, radius, damage, flags) => Rac1World.WeaponDamage.ApplyBlast(weapon, position, radius, damage, flags), (weapon, origin) => Rac1World.Effects.SpawnWeaponExplosion(weapon, origin),
            (cls, sound, position) => FusionWeapons.PlayNativeSound(cls, sound, position)),
        new Rac1WalloperPorts(
            () => _player, () => _rac1Weapons.Equipped, () => PlayerLife.Alive,
            () => Rac1WeaponUsePresentationHost.EquipmentForward(_player), () => Rac1WeaponUsePresentationHost.EquipmentMuzzle(Rac1WeaponId.Walloper, 0, _equipmentPresentation, _avatarPresentation, _player),
            (node, damage) => Rac1World.WeaponDamage.Apply(node, damage),
            (cls, sound, position) => FusionWeapons.PlayNativeSound(cls, sound, position),
            (origin, type, color, size, ticks) => Rac1World.Effects.SpawnEquipmentFlash(origin, type, color, size, ticks)),
        new Rac1TaunterPorts(
            () => (_worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.WorldTick ?? 0), () => Rac1World.Enemies.Targets,
            () => Rac1World.Weapons.Projectiles.Shots.Where(shot => shot.Weapon == Rac1WeaponId.MineGlove)
                .Select(shot => new Rac1TaunterMine(shot.Position, () => shot.Taunted = true)),
            (from, forward) =>
            {
                var ring = _equipmentPresentation.Explosions.CreateEquipmentParticle(27, new(.3f, .7f, 1, .6f), .6f,
                    _equipmentPresentation.NativeViews.Assets, _avatarPresentation.GloveAsset?.ParticleTexturesByPoolType);
                _sceneResult!.Root.AddChild(ring); ring.GlobalPosition = from + forward * .5f;
                Rac1World.Weapons.Projectiles.AddMote(ring, forward * .25f);
            },
            (node, damage) => Rac1World.WeaponDamage.Apply(node, damage)),
        new Rac1TeslaPorts(
            (origin, range, cone, excluded) => Rac1World.Weapons.Contacts.FindArsenalTarget(origin, range, cone, excluded),
            () => (_worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.WorldTick ?? 0),
            (node, damage) => Rac1World.WeaponDamage.Apply(node, damage),
            (cls, sound, origin) => FusionWeapons.PlayNativeSound(cls, sound, origin)),
        new Rac1MorphPorts(
            id => Rac1World.Enemies.Roster.TryGetValue(id, out var enemy) ? enemy : null,
            nativeClass => Rac1World.UsePresentation.CreateEquipmentWorldView(nativeClass),
            enemy => Rac1World.Enemies.IsRac1GameplayActive(enemy.Node),
            enemy => Rac1World.Enemies.TryGroundRac1Enemy(enemy, upwardReach: 1, downwardReach: 1),
            enemy => Rac1World.Enemies.SetRac1EnemyFlame(enemy, false),
            () => Rac1WeaponUsePresentationHost.EquipmentForward(_player),
            (min, max) => _persistentPlayer.Rac1EffectRandom(_args.FixedSeed).Signed(min, max),
            (point, up, down) => Rac1World.Effects.Ground(point, up, down), (origin, type, color, size, ticks) => Rac1World.Effects.SpawnEquipmentFlash(origin, type, color, size, ticks),
            (origin, range, cone) => Rac1World.Weapons.Contacts.FindArsenalTarget(origin, range, cone),
            (from, to, radius) => Rac1World.Weapons.Contacts.Contact(from, to, radius)),
        new Rac1ArsenalBeamPorts(
            () => GetViewport().GetCamera3D(), () => _equipmentPresentation.NativeViews.Assets?.Effects,
            () => _rac1Weapons.Equipped, () => _args.FixedSeed,
            (min, max) => _persistentPlayer.Rac1EffectRandom(_args.FixedSeed).Signed(min, max)),
        new Rac1ArsenalUsePorts(
            () => _rac1Weapons.Equipped, () => _rac1Weapons.GetAmmo(19),
            () => (_worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.WorldTick ?? 0), weapon => Rac1WeaponUsePresentationHost.EquipmentMuzzle(weapon, 0, _equipmentPresentation, _avatarPresentation, _player), () => Rac1WeaponUsePresentationHost.EquipmentForward(_player),
            () => _player!.GlobalPosition, (origin, type, color, size, ticks) => Rac1World.Effects.SpawnEquipmentFlash(origin, type, color, size, ticks)),
        new Rac1EquipmentProjectilePorts(
            () => _player, () => (float)(_avatarPresentation.Avatar?.BaseHeight ?? -.7),
            weapon => Rac1WeaponUsePresentationHost.EquipmentMuzzle(weapon, 0, _equipmentPresentation, _avatarPresentation, _player), () => Rac1WeaponUsePresentationHost.EquipmentForward(_player),
            nativeClass => Rac1World.UsePresentation.CreateEquipmentWorldView(nativeClass),
            (type, tint, scale) => _equipmentPresentation.Explosions.CreateEquipmentParticle(type, tint, scale,
                _equipmentPresentation.NativeViews.Assets, _avatarPresentation.GloveAsset?.ParticleTexturesByPoolType),
            Rac1ExplosionPresenter.EquipmentColor,
            (origin, type, color, size, ticks) => Rac1World.Effects.SpawnEquipmentFlash(origin, type, color, size, ticks), (weapon, origin) => Rac1World.Effects.SpawnWeaponExplosion(weapon, origin),
            (cls, sound, point) => FusionWeapons.PlayNativeSound(cls, sound, point),
            () => Rac1World.Enemies.Targets,
            id => Rac1World.Enemies.Roster.ContainsKey(id),
            (node, damage) => Rac1World.WeaponDamage.Apply(node, damage),
            () => _sceneResult!.Root),
        new Rac1PyrocitorFlamePorts(
            () => Rac1World.Effects.EnsureParticles(),
            () => _equipmentPresentation.Explosions.Particles,
            weapon => Rac1WeaponUsePresentationHost.EquipmentMuzzle(weapon, 0, _equipmentPresentation, _avatarPresentation, _player), () => Rac1WeaponUsePresentationHost.EquipmentForward(_player),
            () => _player, () => (_worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.WorldTick ?? 0),
            node => Rac1World.Enemies.IsRac1GameplayActive(node),
            (node, damage) => Rac1World.WeaponDamage.Apply(node, damage)),
        new Rac1WrenchFramePorts(
            () => _player,
            () => _avatarPresentation.TryWrenchContactPose(out var pose) ? (pose.Hand, pose.Tip) : null,
            () => PlayerLife.Alive,
            () => GodotRac1PlayerAudioPlayback.ResetWrench(_playerAudio),
            () => _rac1HeldItem.HoldWrench(),
            profileId => Rac1WrenchHost.ApplyPlayerAttack(_rac1Nanotech, _player!, profileId),
            () => Rac1WrenchHost.EndPlayerAttack(_rac1Nanotech, _player!),
            (position, spin) => _avatarPresentation.UpdateThrownWrenchVisual(position, spin),
            hit => GodotRac1PlayerAudioPlayback.WrenchWorldContact(_playerAudio, hit),
            () => _playerAudio?.WrenchImpactAdmitted == true,
            (frame, thrown, hand, position) => GodotRac1PlayerAudioPlayback.StepWrench(
                _playerAudio, _avatarPresentation.Controller, frame, thrown, hand, position),
            () => _equipmentPresentation.RefreshRac1HandVisibility(Rac1HandPresentation, _avatarPresentation)),
        new Rac1WrenchContactPorts(
            (previousHand, previousTip, hand, tip, radius, damage, result) =>
            {
                Rac1World.Enemies.Frogs.ResolveRac1FrogCarrierWrenchContacts(previousHand, previousTip, hand, tip, radius);
                Rac1World.Enemies.Frogs.ResolveRac1QueenWrenchContacts(previousHand, previousTip, hand, tip, radius, result);
                ActorHost.Combat.ResolveWrench(previousHand, previousTip, hand, tip, radius, damage);
            },
            () => Rac1World.Presentation.CrateNodes.Concat(Rac1World.Enemies.Targets).Concat(Rac1World.Breakables.NovalisNodes),
            node => Rac1World.Breakables.IsNovalisProp(node)
                ? !Rac1World.Breakables.IsNovalisDestroyed(node.Source.InstanceIndex)
                : Rac1World.Enemies.IsRac1GameplayActive(node),
            id => Rac1World.Weapons.Morph.ViewFor(id),
            (target, result) => Rac1World.WrenchDamage.Strike(target, result),
            soft => _playerAudio?.WrenchContact(soft)),
        new Rac1WrenchDamagePorts(() => _persistentPlayer.Wrench, centre => _persistentPlayer.SelectRac1CrateReward(centre, _args.FixedSeed)),
        new Rac1BombProjectilePorts(
            () => _player, () => _sceneResult is not null,
            () => (float)(_avatarPresentation.Avatar?.BaseHeight ?? -.7),
            () => _persistentPlayer.Rac1EffectRandom(_args.FixedSeed).Angle(),
            () => ActorHost.Active,
            () => ActorHost.Combat.ReadGeometry().Select(target => (target.Node, target.Bounds)),
            FusionWeapons.CreateNativeEffect, Rac1BombProjectileHost.Orient,
            (position, normal, push) => Rac1World.Effects.SpawnBombExplosion(position, impactNormal: normal, reflectedPush: push),
            (generation, id, origin, radius) => ActorHost.Combat.ResolveBlast(FusionWeaponSession.BombGlove,
                generation, id, origin, radius, (float)Rac1BombGlove.NativeDamage, Rac1BombGlove.NativeDamageFlags),
            (node, admission) => Rac1World.WrenchDamage.CompleteCrateDamage(node, admission),
            new(
                () => _equipmentPresentation.Assets(OrdinaryPlayerSession, Rac1World.Loot.PickupAssets) is not null,
                () => PlayerLife.Alive,
                () => _player?.Rac1WrenchFrame.Active == true ||
                    (_worldScenes?.Current?.Resources.Rac1Gameplay?.Wrench.ThrowActive == true),
                point => FusionWeapons.PlayNativeSound(0, 26, point),
                () => PersistRac1CampaignState("Bomb Glove ammo consumption"),
                () => RefreshRac1HudState())),
        new Rac1TaxiHostPorts(
            () => _player is { } player && IsInstanceValid(player) ? new Rac1TaxiPlayerController(player) : null,
            () => InWorldMode && !GetTree().Paused &&
                !PauseMenuOpen && !ControllerSettingsOpen && !Rac1PlanetMapOpen &&
                !QuickSelectOpen && _args.CaptureFrame is null,
            () => _rac1Nanotech.Probe().IsDead),
        owner => CreateRac1EnemyHost(world, scene, owner),
        new Rac1WeaponEffectsPorts(
            _equipmentPresentation,
            () => _persistentPlayer.Rac1EffectRandom(_args.FixedSeed),
            FusionWeapons.CreateNativeEffect,
            point => FusionWeapons.PlayNativeSound(121, 0, point),
            fallback => GetViewport().GetCamera3D()?.GlobalPosition ?? _player?.GlobalPosition ?? fallback,
            () => _sceneResult?.Root,
            () => _avatarPresentation.GloveAsset?.ParticleTexturesByPoolType,
            () => !OrdinaryPlayerSession && !_args.Rac1WeaponsSmoke && !_args.Rac1BombGloveSmoke,
            () => MovementReproduction.IsLoading,
            () => _equipmentPresentation.Assets(OrdinaryPlayerSession, Rac1World.Loot.PickupAssets) is not null,
            () => _player is not null,
            () => _persistentPlayer.Rac1Use.PyroEmitting,
            () => _args.FixedSeed),
        new Rac1WeaponUsePorts(
            _persistentPlayer, _equipmentPresentation, _avatarPresentation,
            new(() => _player, () => _playerAudio, () => _rac1Weapons, () => _rac1Campaign.Session.Weapons,
                () => FusionWeapons, () => _world, () => _sceneResult!.Root,
                () => Sources.Setup.Library.Get(ObpSourceGame.Rac1)!.Path),
            new(() => OrdinaryPlayerSession, () => MovementReproduction.IsLoading, () => PlayerLife.Alive,
                () => _args.Rac1WeaponsSmoke, () => _args.FixedSeed),
            new(MovementReproduction.Report, PersistRac1CampaignState,
                () => RefreshRac1HudState(),
                () => _equipmentPresentation.HoldSelectedRac1HandItem(Rac1HandPresentation, _avatarPresentation),
                () => _equipmentPresentation.RefreshRac1HandVisibility(Rac1HandPresentation, _avatarPresentation),
                item => PlayerEquipmentPresentationBinder.IsRac1HandEquipmentVisible(item, Rac1HandPresentation))));

    private Rac1EnemyHost CreateRac1EnemyHost(RuntimeWorld world, RuntimeWorldScene.Result scene, Rac1WorldModule owner) => new(
        world, scene, owner.Context, owner.Presentation, new(
            () => Sources.Setup.Library.Get(ObpSourceGame.Rac1)?.Path ?? throw new InvalidOperationException("Enemy assets require the active R&C1 source."),
            () => _persistentPlayer.Rac1EffectRandom(_args.FixedSeed),
            new(() => _player, () => PlayerLife.Alive, () => _playerNative is not null, () => _rac1Nanotech,
                (attacker, tick, amount, flags) => PlayerLife.ApplySourceDamage(attacker, OBP.RAC1.Rac1Authority.Primary.BuildId,
                    PlayerSessionNativeState.Rac1DamageAdapter, PlayerSessionNativeState.Rac1DamageDescriptor, tick, amount, flags),
                (before, after) => _playerAudio?.NotifyDamage(before, after), feedback => RefreshRac1HudState(feedback),
                (attacker, tick, amount, flags) => PlayerLife.ApplySourceDamage(attacker, OBP.RAC1.Rac1Authority.Primary.BuildId,
                    PlayerSessionNativeState.AridiaDamageAdapter, PlayerSessionNativeState.AridiaDamageDescriptor, tick, amount, flags)),
            new((enemy, player) => owner.Weapons.Deployables.EnemyGoal(enemy, player),
                enemy => owner.Weapons.Deployables.EnemyHitsDecoy(enemy),
                enemy => owner.Weapons.Deployables.IsDecoyNearby(enemy),
                id => owner.Weapons.Projectiles.MinePosition(id),
                id => owner.Weapons.Projectiles.DoomPosition(id),
                centre => _persistentPlayer.SelectRac1CrateReward(centre, _args.FixedSeed), owner.Loot.SpawnRac1BoltPickups, (node, ids) => ActorHost.TrackPickups(node, ids),
                nativeClass => owner.Loot.HasPickupModel(nativeClass),
                (nativeClass, motion) =>
                {
                    var node = owner.Loot.CreateRac1EffectNode(nativeClass, $"rac1_enemy_fragment_{nativeClass}");
                    node.GlobalPosition = Rac1LootPresentation.FromEffectNative(motion.Position); owner.Loot.AddDebris(node, motion);
                }, owner.Loot.PlayRac1PickupSound, (node, sound) => ActorHost.PlayRac1Sound(node, sound), (from, to) => Rac1World.Weapons.Contacts.HasLineOfSight(from, to), origin => Rac1World.Effects.SpawnBombExplosion(origin),
                origin => Rac1World.Effects.SpawnExplosionBursts(origin, Rac1BombExplosion.Bursts(false).Take(1).ToArray()),
                (node, damage) => owner.Breakables.TryTriggerExplosiveDamage(node, damage),
            (node, admission) => owner.WrenchDamage.CompleteCrateDamage(node, admission),
                (id, presence) => { if (owner.Loot.CrateBodies.TryGetValue(id, out var body) && IsInstanceValid(body)) body.CollisionLayer = presence == RuntimeEntityPresence.Active ? 1u : 0; },
                id => Rac1World.Weapons.Morph.Contains(id)),
            new(() => ReferenceEquals(_worldScenes?.Current?.Resources.Rac1Gameplay, owner)),
            new(GetViewport, () => _activeCamera, camera => _activeCamera = camera, () => _camera,
                () => _worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.GuidedCamera.Camera, owner.Wrench.HasVictim,
                key => { owner.Wrench.AdmitVictim(key); },
                (nativeClass, name) => owner.Loot.CreateRac1EffectNode(nativeClass, name),
                (node, debris) => owner.Loot.AddDebris(node, debris), Rac1LootPresentation.FromEffectOrientation)));
}
