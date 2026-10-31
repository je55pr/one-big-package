using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

/// <summary>
/// Borrowed source/presentation operations. The current world lease controls effect
/// admission; the equipment binder retains the authoritative visual resources.
/// </summary>
internal sealed record Rac1WeaponEffectsPorts(
    PlayerEquipmentPresentationBinder Equipment,
    Func<Rac1PickupRandom> Random,
    Func<int, string, Node3D> CreateEffect,
    Action<Vector3> BombSound,
    Func<Vector3, Vector3> Camera,
    Func<Node3D?> SceneRoot,
    Func<IReadOnlyDictionary<int, RuntimeTexture>?> GloveTextures,
    Func<bool> EnforceLevelBounds,
    Func<bool> Loading,
    Func<bool> AssetsReady,
    Func<bool> HasPlayer,
    Func<bool> PyroEmitting,
    Func<int?> Seed);

/// <summary>
/// R&C1 world-bound explosion, flash and weapon-effect presentation. World damage,
/// native projectile motion and persistent inventory remain their original owners.
/// </summary>
internal sealed class Rac1WeaponEffectsHost(Rac1WorldModule world, Rac1WeaponEffectsPorts ports)
{
    private Rac1ExplosionPresenter Explosions => ports.Equipment.Explosions;
    private Rac1EquipmentAssets.Catalogue? Assets => ports.Equipment.NativeViews.Assets;

    internal Rac1ParticleSession? EnsureParticles() =>
        Explosions.EnsureParticles(Assets, ports.Seed());

    internal static void ResetOrFallback(Rac1WorldModule? world, PlayerEquipmentPresentationBinder equipment)
    {
        if (world is { } active) active.Effects.ResetPresentation();
        else equipment.Explosions.RetireWorld();
    }

    internal void ResetPresentation()
    {
        Explosions.RetireWorld();
        world.Weapons.ThrownAnimation.Reset();
        world.Bombs.ResetImpactCount();
    }

    internal void SpawnBombExplosion(Vector3 origin, bool playSound = true,
        Vector3? impactNormal = null, Vector3? reflectedPush = null) =>
        Explosions.SpawnBomb(origin, ports.Camera(origin), ports.Random(), ports.CreateEffect,
            ports.BombSound, Rac1BombProjectileHost.Orient, Assets, ports.Seed(),
            playSound, impactNormal, reflectedPush);

    internal void SpawnWeaponExplosion(Rac1WeaponId weapon, Vector3 origin)
    {
        bool groundedMine = weapon == Rac1WeaponId.MineGlove &&
            Ground(origin, .5f, 2.5f) is { } ground && origin.Y - ground.Y <= 2;
        Explosions.SpawnWeapon(weapon, origin, ports.Camera(origin), groundedMine,
            ports.Random(), ports.CreateEffect, Rac1BombProjectileHost.Orient, Assets, ports.Seed());
    }

    internal void SpawnExplosionBursts(Vector3 origin, IReadOnlyList<Rac1BombExplosion.Burst> bursts) =>
        Explosions.SpawnBursts(origin, bursts, ports.Random(), ports.CreateEffect);

    internal void StepBombEffectsPhysics() =>
        Explosions.Step(ports.SceneRoot(), ports.Camera(Vector3.Zero), Assets,
            ports.Seed(), ports.EnforceLevelBounds(), Rac1BombProjectileHost.Orient);

    internal void SpawnEquipmentFlash(Vector3 origin, int type, Color color, float size, int ticks)
    {
        var node = Explosions.CreateEquipmentParticle(type, color, size,
            Assets, ports.GloveTextures());
        ports.SceneRoot()!.AddChild(node);
        node.GlobalPosition = origin;
        world.Weapons.Projectiles.AddFlash(node, ticks, size);
    }

    internal void StepAdditionalWeaponWorld()
    {
        if (ports.Loading() || !ports.AssetsReady() || !ports.HasPlayer()) return;
        world.Weapons.StepWorld(ports.PyroEmitting());
    }

    internal Vector3? Ground(Vector3 point, float up, float down) =>
        world.Weapons.Projectiles.Ground(point, up, down);
}
