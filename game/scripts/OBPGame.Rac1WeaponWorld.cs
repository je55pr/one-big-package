using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

public partial class OBPGame
{
    private Rac1ParticleSession? EnsureRac1Particles()
        => _equipmentPresentation.Explosions.EnsureParticles(_rac1EquipmentAssets, _args.FixedSeed);

    private void ResetRac1BombPresentation()
    {
        _equipmentPresentation.Explosions.RetireWorld();
        _worldScenes?.Current?.Resources.Rac1Gameplay?.Weapons.ThrownAnimation.Reset();
        _worldScenes?.Current?.Resources.Rac1Gameplay?.Bombs.ResetImpactCount();
    }

    private Vector3 Rac1EffectCamera(Vector3 fallback) =>
        GetViewport().GetCamera3D()?.GlobalPosition ?? _player?.GlobalPosition ?? fallback;

    private void SpawnRac1BombExplosion(Vector3 origin, bool playSound = true, Vector3? impactNormal = null, Vector3? reflectedPush = null)
        => _equipmentPresentation.Explosions.SpawnBomb(origin, Rac1EffectCamera(origin), _persistentPlayer.Rac1EffectRandom(_args.FixedSeed),
            FusionWeapons.CreateNativeEffect, point => FusionWeapons.PlayNativeSound(121, 0, point),
            SetBombOrientation, _rac1EquipmentAssets, _args.FixedSeed, playSound, impactNormal, reflectedPush);

    private void SpawnRac1WeaponExplosion(Rac1WeaponId weapon, Vector3 origin)
    {
        bool groundedMine = weapon == Rac1WeaponId.MineGlove &&
            EquipmentGround(origin, .5f, 2.5f) is { } ground && origin.Y - ground.Y <= 2;
        _equipmentPresentation.Explosions.SpawnWeapon(weapon, origin, Rac1EffectCamera(origin), groundedMine,
            _persistentPlayer.Rac1EffectRandom(_args.FixedSeed), FusionWeapons.CreateNativeEffect, SetBombOrientation, _rac1EquipmentAssets, _args.FixedSeed);
    }

    private void SpawnRac1ExplosionBursts(Vector3 origin, IReadOnlyList<Rac1BombExplosion.Burst> bursts)
        => _equipmentPresentation.Explosions.SpawnBursts(origin, bursts, _persistentPlayer.Rac1EffectRandom(_args.FixedSeed), FusionWeapons.CreateNativeEffect);

    private void StepRac1BombEffectsPhysics()
        => _equipmentPresentation.Explosions.Step(_sceneResult?.Root, Rac1EffectCamera(Vector3.Zero),
            _rac1EquipmentAssets, _args.FixedSeed,
            !OrdinaryPlayerSession && !_args.Rac1WeaponsSmoke && !_args.Rac1BombGloveSmoke, SetBombOrientation);

    private QuadMesh GetRac1ParticleMesh(int poolType)
        => _equipmentPresentation.Explosions.GetParticleMesh(poolType, _rac1EquipmentAssets?.ParticleTextures,
            _avatarPresentation.GloveAsset?.ParticleTexturesByPoolType);

    private ulong _rac1WeaponWorldTick;
    private static Color EquipmentColor(uint rgba) => new((rgba & 255) / 128f, ((rgba >> 8) & 255) / 128f,
        ((rgba >> 16) & 255) / 128f, ((rgba >> 24) & 255) / 128f);

    private Node3D EquipmentParticle(int poolType, Color color, float size)
    {
        var node = new MeshInstance3D
        {
            Mesh = GetRac1ParticleMesh(poolType),
            Scale = Vector3.One * size,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        node.SetInstanceShaderParameter("tint", color);
        return node;
    }

    private void SpawnEquipmentFlash(Vector3 origin, int type, Color color, float size, int ticks)
    {
        var node = EquipmentParticle(type, color, size); _sceneResult!.Root.AddChild(node); node.GlobalPosition = origin;
        Rac1World.Weapons.Projectiles.AddFlash(node, ticks, size);
    }

    private void StepRac1AdditionalWeaponWorld()
    {
        if (MovementReproduction.IsLoading || _equipmentPresentation.Assets(OrdinaryPlayerSession, Rac1World.Loot.PickupAssets) is null || _player is null) return;
        _rac1WeaponWorldTick++;
        StepRac1ArsenalWorld();
        Rac1World.Weapons.Flames.Emit(_persistentPlayer.Rac1Use.PyroEmitting);
        Rac1World.Weapons.Projectiles.StepMotes();
        Rac1World.Weapons.Flames.StepContacts();
        Rac1World.Weapons.SuckCannon.Step();
        Rac1World.Weapons.Projectiles.StepShots();
        Rac1World.Weapons.Projectiles.StepDoomAgents();
        Rac1World.Weapons.Projectiles.StepFlashes();
    }

    private Vector3? EquipmentGround(Vector3 point, float up, float down)
        => Rac1World.Weapons.Projectiles.Ground(point, up, down);
}
