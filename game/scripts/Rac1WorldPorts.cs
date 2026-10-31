using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Presentation;

namespace OBP.Game;

// Temporary bindings to the existing mechanic owners. These ports carry no root
// reference and can be supplied by the independently extracted source modules.
internal sealed record Rac1WorldResetPorts(Action Player, Action Wheel,
    Action Retry, Action Pickups, Action GoldBolts,
    Action AdditionalWeapons, Action BombPresentation);

internal sealed record Rac1WorldActorPorts(Action<RuntimeWorld> Pickups,
    Action<RuntimeWorldScene.DynamicObjectNode> CrateCollider,
    Action<Rac1CheckpointHost> Checkpoints,
    Action<RuntimeWorld, RuntimeWorldScene.Result> PlacedBolts,
    Action<RuntimeWorld, RuntimeWorldScene.Result> GoldBolts);

internal sealed record Rac1WorldTickPorts(Func<bool> Suspended,
    Action<Rac1CheckpointHost> Checkpoints, Action<double> Pickups, Action GoldBolts);

// Borrow only the currently selected player/campaign and narrow UI/navigation
// actions. Native mission actors, pending scenes and deferred exits live in the
// world lease rather than in the application root.
internal sealed record Rac1MissionWorldPorts(
    Func<string?> SourcePath, Func<Rac1CampaignHost> Campaign,
    Func<Rac1BoltWallet> Wallet, Func<Rac1RatchetNanotechSession> Health,
    Func<PlayerHost?> Player, Func<bool> QuickSelectOpen,
    Func<bool> Paused, Action<bool> Pause,
    Action<Label> AttachPrompt, Action<Rac1MissionSceneUi> AttachScene,
    Action<string> Persist, Action RefreshHud, Action<ObpDestination> Navigate,
    Func<bool> WorldStillCurrent);

internal sealed record Rac1PlayerGameplayPorts(Func<bool> Ordinary, Func<bool> Persistent, Func<bool> Alive,
    Func<Rac1RatchetNanotechSession> Health, Func<Rac1WeaponInventory> Weapons,
    Action RefreshMovement, Action PrimaryAttack, Action<Rac1WeaponId> SelectWeapon, Action<int> CycleWeapon,
    Action PreviewWeapon, Action<Rac1WrenchAttackSession.Frame> WrenchPhysics,
    Action<RuntimeWorld> ConfigureWeapons, Func<bool> HasFusion, Action<RuntimeWorld> ConfigurePresentation,
    Action ResetParityHealth, Func<bool> EnvironmentalRestart, Action EnvironmentalDeath, Action CombatDeath);

internal sealed record Rac1WorldContactPorts(Action Drowned, Action<int> WaterSound);

internal sealed record Rac1GameplayHudPorts(HudStateAdapter State, Func<bool> Ordinary, Func<bool> HasPlayer,
    Action<HudFeedbackDraft[]> PublishPersistent, Func<HudPresentationState, HudPresentationState> ProjectEquipment,
    Func<Rac1RatchetNanotechSession> Health, Func<Rac1WeaponInventory> Weapons, Func<Rac1BoltWallet> Wallet,
    Func<int> Cargo, Func<PlayerHost?> Player, Func<string> MetalDetectorText);