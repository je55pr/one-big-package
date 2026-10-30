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
        if (_rac1PyroEmitting && EnsureRac1Particles() is { } particles)
        {
            Vector3 nozzle = EquipmentMuzzle(Rac1WeaponId.Pyrocitor);
            float reach = Rac1WeaponProfiles.PyrocitorReach;
            var ray = PhysicsRayQueryParameters3D.Create(nozzle, nozzle + EquipmentForward * (reach + .75f));
            ray.Exclude = Rac1World.Weapons.Contacts.RayExclusions();
            var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(ray);
            if (hit.TryGetValue("position", out var contact)) reach = Math.Max(0, nozzle.DistanceTo(contact.AsVector3()) - .75f);
            var created = particles.Pyrocitor(Rac1LootPresentation.ToEffectNative(nozzle), Rac1LootPresentation.ToEffectNative(EquipmentForward), reach);
            // 0x2b8438 owns fifteen tracked slots. Only emitter index2 on every
            // fourth update is kept for the travelling damage-volume queries.
            if ((_rac1WeaponWorldTick & 3) == 0 && created.Count > 2)
            {
                if (_rac1NativeFlames.Count == 15) _rac1NativeFlames.RemoveAt(0);
                _rac1NativeFlames.Add(new(created[2]));
            }
        }
        Rac1World.Weapons.Projectiles.StepMotes();
        StepRac1NativeFlameContacts();
        Rac1World.Weapons.SuckCannon.Step();
        Rac1World.Weapons.Projectiles.StepShots();
        Rac1World.Weapons.Projectiles.StepDoomAgents();
        Rac1World.Weapons.Projectiles.StepFlashes();
    }

    private Vector3? EquipmentGround(Vector3 point, float up, float down)
        => Rac1World.Weapons.Projectiles.Ground(point, up, down);

    private void StepRac1NativeFlameContacts()
    {
        if (_equipmentPresentation.Explosions.Particles is not { } particles) return;
        foreach (var flame in _rac1NativeFlames.ToArray())
        {
            var p = flame.Motion;
            if (!particles.IsLive(p) || p.Remaining < 2) { _rac1NativeFlames.Remove(flame); continue; }
            particles.FlameSecondary(p);
            Vector3 point = Rac1LootPresentation.FromEffectNative(p.Position);
            float radius = p.Size * .5f;
            foreach (var (victim, bounds) in Rac1World.Weapons.Contacts.Geometry)
            {
                var key = new Rac1MobyRuntimeKey(victim.Source.NativeClassId, victim.Source.InstanceIndex);
                if (!Rac1World.Enemies.IsRac1GameplayActive(victim) || flame.Victims.Contains(key) || _rac1FlameHitTicks.GetValueOrDefault(key) > _rac1WeaponWorldTick) continue;
                if (!bounds.Grow(radius).HasPoint(point) || !Rac1World.Weapons.Contacts.HasLineOfSight(point, victim.Root.GlobalPosition + Vector3.Up * .4f)) continue;
                if (Rac1World.WeaponDamage.Apply(victim, Rac1WeaponProfiles.DirectDamage(Rac1WeaponId.Pyrocitor, key,
                    Rac1WeaponProfiles.PyrocitorDamage, Rac1WeaponProfiles.PyrocitorFlags)))
                {
                    // Victim repetition remains the existing bounded damage
                    // adapter; it is separate from the recovered particle life.
                    flame.Victims.Add(key); _rac1FlameHitTicks[key] = _rac1WeaponWorldTick + Rac1WeaponReconstructionPolicy.FlameVictimRepeatTicks;
                }
            }
        }
    }


}
