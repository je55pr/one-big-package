using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Progression;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// Typed boundary between a legacy R&C1 movement bookmark and the active
/// world lease. The host supplies only its campaign/visual side effects.
/// </summary>
internal sealed record Rac1MovementWorldPorts(
    Func<Rac1WorldModule> World,
    Func<RuntimeWorldScene.Result?> Scene,
    Func<Rac1WeaponInventory> Weapons,
    Func<Rac1BoltWallet> Wallet,
    Func<Rac1AdditionalWeaponSession?> AdditionalWeapons,
    Func<bool> WeaponPreview,
    Func<IReadOnlyList<Rac1MobyRuntimeKey>> MorphedMobies,
    ISet<Rac1MobyRuntimeKey> ChickenCargo,
    Action RetireCapturedCargoPresentation,
    Func<int?> HeldItemId,
    Action<RuntimeWorldScene.DynamicObjectNode> MorphEnemy,
    Action<int> DisableCrateCollider,
    Action<int> RemovePlacedBolt);

internal sealed class Rac1MovementWorldStateAdapter(Rac1MovementWorldPorts ports)
{
    public MovementReproWorld Capture()
    {
        var module = ports.World();
        var runtime = module.Context.MobyRuntime;
        var enemies = module.Enemies;
        return new MovementReproWorld(
            runtime.Instances
                .Where(instance => !instance.IsActive ||
                    enemies.Roster.TryGetValue(instance.InstanceIndex, out var enemy) && enemy.RewardEmitted)
                .OrderBy(instance => instance.InstanceIndex)
                .Select(instance => new MovementReproInactiveObject(instance.Key, instance.State.NativeState,
                    instance.State.IsTerminalized ||
                    enemies.Roster.TryGetValue(instance.InstanceIndex, out var enemy) && enemy.RewardEmitted)).ToArray(),
            ports.Weapons().CapturePersistentState(), ports.Weapons().CurrentItemId, ports.Wallet().Balance,
            ports.AdditionalWeapons()?.Cargo.ToArray(), ports.WeaponPreview(), ports.MorphedMobies())
        {
            HeldItemId = ports.HeldItemId(),
        };
    }

    public void Restore(MovementReproWorld world)
    {
        world.Validate();
        var module = ports.World();
        var runtime = module.Context.MobyRuntime;
        var enemies = module.Enemies;
        var scene = ports.Scene() ?? throw new InvalidOperationException("No scene lease for movement-state restore.");

        // Check every reference before mutating the live world. A mismatched
        // state is rejected rather than half-restoring its native objects.
        var objects = world.InactiveObjects.Select(saved =>
            (Saved: saved, Instance: runtime.TryGet(saved.Key, out var instance) ? instance! :
                throw new InvalidDataException($"Saved object {saved.Key} is unavailable in this world."))).ToArray();
        foreach (var key in world.MorphedMobies ?? [])
            if (!enemies.Roster.TryGetValue(key.InstanceIndex, out var enemy) || enemy.Node.Source.NativeClassId != key.NativeClassId)
                throw new InvalidDataException("Saved Morph-o-Ray victim is unavailable in this level.");

        // Restoration replaces, rather than accumulates, captured cargo and its
        // presentation/morph metadata. Retire stale visuals before importing the save.
        ports.RetireCapturedCargoPresentation();
        ports.ChickenCargo.Clear();
        ports.AdditionalWeapons()?.RestoreCargo(world.CapturedMobies ?? []);
        foreach (var key in world.MorphedMobies ?? [])
        {
            var enemy = enemies.Roster[key.InstanceIndex];
            if (world.CapturedMobies?.Contains(key) == true) ports.ChickenCargo.Add(key);
            else ports.MorphEnemy(enemy.Node);
        }

        foreach (var (saved, instance) in objects)
        {
            runtime.SetNativeState(instance, saved.NativeState);
            if (saved.Terminalized && !instance.State.IsTerminalized) runtime.Terminalize(instance);
            var node = Rac1WorldPresentation.FindPresentedDynamic(scene, instance.Source);
            if (node is not null) enemies.SetRac1HostedPresence(node, RuntimeEntityPresence.Inactive);
            else runtime.SetPresence(instance, RuntimeEntityPresence.Inactive);
            if (enemies.Roster.TryGetValue(instance.InstanceIndex, out var enemy))
            {
                enemy.RewardEmitted = enemy.DeathPresented = saved.Terminalized;
                enemy.Body.CollisionLayer = 0;
                if (saved.Terminalized) enemy.Body.CollisionMask = 0;
                enemies.SetRac1EnemyFlame(enemy, false);
            }
            if (Rac1BoltCrate.IsCrateClass(instance.NativeClassId))
            {
                ports.DisableCrateCollider(instance.InstanceIndex);
                if (saved.Terminalized)
                {
                    var authored = Rac1BoltCrate.ReadAuthored(instance.Source)!;
                    module.Context.MobyPersistence.UpdateUid(instance.Source with { NativeUid = authored.Uid },
                        Rac1MobyUidPersistenceBits.BothSet);
                }
            }
            if (Rac1PlacedBolt.IsBoltClass(instance.NativeClassId))
                ports.RemovePlacedBolt(Rac1PlacedBoltSession.PickupId(instance.InstanceIndex));
        }
    }
}
