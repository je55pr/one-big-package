using Godot;
using OBP.Core;
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
        _rac1BombThrowGeneration = 0; _rac1BombThrowActive = false; _rac1BombImpactCount = 0;
    }

    private Vector3 Rac1EffectCamera(Vector3 fallback) =>
        GetViewport().GetCamera3D()?.GlobalPosition ?? _player?.GlobalPosition ?? fallback;

    private void SpawnRac1BombExplosion(Vector3 origin, bool playSound = true, Vector3? impactNormal = null, Vector3? reflectedPush = null)
        => _equipmentPresentation.Explosions.SpawnBomb(origin, Rac1EffectCamera(origin), Rac1EffectRandom,
            FusionWeapons.CreateNativeEffect, point => FusionWeapons.PlayNativeSound(121, 0, point),
            SetBombOrientation, _rac1EquipmentAssets, _args.FixedSeed, playSound, impactNormal, reflectedPush);

    private void SpawnRac1WeaponExplosion(Rac1WeaponId weapon, Vector3 origin)
    {
        bool groundedMine = weapon == Rac1WeaponId.MineGlove &&
            EquipmentGround(origin, .5f, 2.5f) is { } ground && origin.Y - ground.Y <= 2;
        _equipmentPresentation.Explosions.SpawnWeapon(weapon, origin, Rac1EffectCamera(origin), groundedMine,
            Rac1EffectRandom, FusionWeapons.CreateNativeEffect, SetBombOrientation, _rac1EquipmentAssets, _args.FixedSeed);
    }

    private void SpawnRac1ExplosionBursts(Vector3 origin, IReadOnlyList<Rac1BombExplosion.Burst> bursts)
        => _equipmentPresentation.Explosions.SpawnBursts(origin, bursts, Rac1EffectRandom, FusionWeapons.CreateNativeEffect);

    private void StepRac1BombEffectsPhysics()
        => _equipmentPresentation.Explosions.Step(_sceneResult?.Root, Rac1EffectCamera(Vector3.Zero),
            _rac1EquipmentAssets, _args.FixedSeed,
            !OrdinaryPlayerSession && !_args.Rac1WeaponsSmoke && !_args.Rac1BombGloveSmoke, SetBombOrientation);

    private QuadMesh GetRac1ParticleMesh(int poolType)
        => _equipmentPresentation.Explosions.GetParticleMesh(poolType, _rac1EquipmentAssets?.ParticleTextures,
            _avatarPresentation.GloveAsset?.ParticleTexturesByPoolType);

    private readonly List<(Node3D Node, int Age, int Lifetime, float Size)> _rac1WeaponFlashes = [];
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
        _rac1WeaponFlashes.Add((node, 0, ticks, size));
    }

    private void StepRac1AdditionalWeaponWorld()
    {
        if (MovementReproduction.IsLoading || _equipmentPresentation.Assets(OrdinaryPlayerSession, _rac1PickupAssets) is null || _player is null) return;
        _rac1WeaponWorldTick++;
        StepRac1ArsenalWorld();
        if (_rac1PyroEmitting && EnsureRac1Particles() is { } particles)
        {
            Vector3 nozzle = EquipmentMuzzle(Rac1WeaponId.Pyrocitor);
            float reach = Rac1WeaponProfiles.PyrocitorReach;
            var ray = PhysicsRayQueryParameters3D.Create(nozzle, nozzle + EquipmentForward * (reach + .75f));
            ray.Exclude = Rac1DamageableRayExclusions();
            var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(ray);
            if (hit.TryGetValue("position", out var contact)) reach = Math.Max(0, nozzle.DistanceTo(contact.AsVector3()) - .75f);
            var created = particles.Pyrocitor(ToEffectNative(nozzle), ToEffectNative(EquipmentForward), reach);
            // 0x2b8438 owns fifteen tracked slots. Only emitter index2 on every
            // fourth update is kept for the travelling damage-volume queries.
            if ((_rac1WeaponWorldTick & 3) == 0 && created.Count > 2)
            {
                if (_rac1NativeFlames.Count == 15)
                {
                    _actorHost?.Combat.ForgetFlame(_rac1NativeFlames[0].Motion);
                    _rac1NativeFlames.RemoveAt(0);
                }
                _rac1NativeFlames.Add(new(created[2]));
            }
        }
        StepRac1WeaponMotes();
        StepRac1NativeFlameContacts();
        StepRac1Vacuum();
        foreach (var shot in _rac1EquipmentShots.ToArray())
        {
            if (!IsInstanceValid(shot.Node)) { _rac1EquipmentShots.Remove(shot); continue; }
            shot.Age++;
            if (shot.Weapon is Rac1WeaponId.Blaster or Rac1WeaponId.SuckCannon)
            {
                Vector3 next = shot.Position + shot.Step;
                if (shot.Motion is { } motion)
                {
                    motion.Step(shot.AimPoint, 1.5f * MathF.PI);
                    next = FromEffectNative(motion.Position);
                }
                int lifetime = shot.Weapon == Rac1WeaponId.Blaster ? Rac1WeaponProfiles.BlasterCarrierTicks : Rac1WeaponReconstructionPolicy.SuckProjectileLifetimeTicks;
                if (shot.Age > lifetime)
                {
                    RetireEquipmentShot(shot); continue;
                }
                if (EquipmentContact(shot.Position, next, shot.Weapon == Rac1WeaponId.Blaster ? .15f : .25f) is { } hit)
                {
                    if (hit.Target is { } victim)
                        ApplyRac1AdditionalDamage(victim, Rac1WeaponProfiles.DirectDamage(shot.Weapon,
                            new(victim.Source.NativeClassId, victim.Source.InstanceIndex),
                            shot.Weapon == Rac1WeaponId.Blaster ? Rac1WeaponProfiles.BlasterDamage : Rac1WeaponReconstructionPolicy.SuckDamage,
                            shot.Weapon == Rac1WeaponId.Blaster ? Rac1WeaponProfiles.BlasterFlags : Rac1WeaponReconstructionPolicy.SuckDamageFlags));
                    SpawnEquipmentFlash(hit.Point, 27, new(.7f, .8f, 1, 1), .6f, 10);
                    FusionWeapons.PlayNativeSound(849, 0, hit.Point); RetireEquipmentShot(shot); continue;
                }
                shot.Position = next; shot.Node.GlobalPosition = next;
                if (shot.Weapon == Rac1WeaponId.SuckCannon) shot.Node.RotateY(.25f);
                continue;
            }
            if (shot.Node is Rac1EquipmentView skin)
            {
                if (shot.Weapon == Rac1WeaponId.GloveOfDoom)
                    skin.Scale = Vector3.One * Math.Min(Rac1ArsenalRules.DoomCanisterThrownScale, skin.Scale.X + Rac1ArsenalRules.DoomCanisterGrowth);
                foreach (int sound in skin.Step()) FusionWeapons.PlayNativeSound(shot.Weapon == Rac1WeaponId.MineGlove ? 74 : 230, sound, shot.Position);
                if (shot.Grounded)
                { shot.Position = new(shot.Position.X, shot.GroundHeight + skin.FootOffset * skin.Scale.Y, shot.Position.Z); shot.Node.GlobalPosition = shot.Position; }
            }
            if (!shot.Grounded)
            {
                Vector3 next = shot.Position + shot.Step;
                shot.Step -= Vector3.Up * OBP.RAC1.Gameplay.Rac1BombProjectileSession.GravityStep;
                if (EquipmentContact(shot.Position, next, .12f) is { } hit)
                {
                    if (shot.Weapon == Rac1WeaponId.MineGlove && hit.Target is { } directVictim &&
                        Rac1World.Enemies.Roster.ContainsKey(directVictim.Source.InstanceIndex))
                    { shot.Position = hit.Point; ExplodeEquipment(shot, 2, 3, Rac1WeaponProfiles.MineFlags, 74); continue; }
                    shot.Position = hit.Point + Vector3.Up * (((shot.Node as Rac1EquipmentView)?.FootOffset ?? .06f) * shot.Node.Scale.Y);
                    shot.GroundHeight = hit.Point.Y;
                    shot.Grounded = true; shot.Step = Vector3.Zero;
                    if (shot.Node is Rac1EquipmentView mine && shot.Weapon == Rac1WeaponId.MineGlove)
                        mine.Play(1, false, shot.Id, 2);
                    FusionWeapons.PlayNativeSound(shot.Weapon == Rac1WeaponId.MineGlove ? 74 : 230, 0, hit.Point);
                }
                else { shot.Position = next; shot.Node.RotateY(.15f); }
                shot.Node.GlobalPosition = shot.Position;
            }
            else if (shot.Canister is { } canister)
            {
                if (canister.Step(_rac1DoomAgents.Count)) SpawnRac1DoomAgent(shot, canister.Emitted - 1);
                if (canister.Finished) { RetireEquipmentShot(shot); continue; }
            }
            else
            {
                // Grounded mine proximity/seek is an OBP query adapter. Native
                // class74 animation and damage descriptors remain source-owned.
                var enemy = Rac1World.Enemies.Targets.Where(IsRac1WeaponAimTarget)
                    .OrderBy(node => node.Root.GlobalPosition.DistanceSquaredTo(shot.Position)).FirstOrDefault();
                if (enemy is not null && shot.Age > 20)
                {
                    Vector3 goal = enemy.Root.GlobalPosition + Vector3.Up * .2f;
                    float distance = goal.DistanceTo(shot.Position);
                    if (distance < Rac1WeaponReconstructionPolicy.MineDetonationDistance * (shot.Taunted ? 3 : 1) && BombHasLineOfSight(shot.Position + Vector3.Up * .15f, goal))
                    { ExplodeEquipment(shot, 2, 3, Rac1WeaponProfiles.MineFlags, 74); continue; }
                    if (distance < Rac1WeaponReconstructionPolicy.MineSeekRange * (shot.Taunted ? 3 : 1) && BombHasLineOfSight(shot.Position + Vector3.Up * .2f, goal))
                    {
                        Vector3 step = (goal - shot.Position).Slide(Vector3.Up).Normalized() * Rac1WeaponReconstructionPolicy.MineSeekStep;
                        if (EquipmentGround(shot.Position + step, .5f, 1.2f) is { } ground)
                        { shot.GroundHeight = ground.Y; shot.Position = ground + Vector3.Up * ((shot.Node as Rac1EquipmentView)?.FootOffset ?? .06f); shot.Node.GlobalPosition = shot.Position; }
                    }
                }
            }
            if (shot.Age > Rac1WeaponReconstructionPolicy.PlantedLifetimeTicks || shot.Position.Y < -100) RetireEquipmentShot(shot);
        }
        StepRac1DoomAgents();
        for (int i = _rac1WeaponFlashes.Count - 1; i >= 0; i--)
        {
            var item = _rac1WeaponFlashes[i];
            if (!IsInstanceValid(item.Node) || ++item.Age >= item.Lifetime)
            {
                if (IsInstanceValid(item.Node)) item.Node.QueueFree();
                _rac1WeaponFlashes.RemoveAt(i); continue;
            }
            item.Node.Scale = Vector3.One * item.Size * (1 - item.Age / (float)item.Lifetime);
            _rac1WeaponFlashes[i] = item;
        }
    }

    private void StepRac1WeaponMotes()
    {
        foreach (var mote in _rac1WeaponMotes.ToArray())
        {
            if (!IsInstanceValid(mote.Node)) { _rac1WeaponMotes.Remove(mote); continue; }
            var from = mote.Node.GlobalPosition; var next = from + mote.Step;
            // The remaining Taunter/Suck presentation motes are not the native
            // flame pool and cannot submit Pyrocitor damage.
            if (++mote.Age > Rac1WeaponReconstructionPolicy.FlameLifetimeTicks || !BombHasLineOfSight(from, next))
            { mote.Node.QueueFree(); _rac1WeaponMotes.Remove(mote); continue; }
            mote.Node.GlobalPosition = next;
            float radius = .12f + mote.Age * .025f;
            mote.Node.Scale = Vector3.One * radius * 2;
        }
    }

    private void StepRac1NativeFlameContacts()
    {
        if (_rac1NativeFlames.Count == 0 ||
            _equipmentPresentation.Explosions.Particles is not { } particles) return;
        // A single actor geometry read for the entire native flame tick; a
        // stale hit cannot land because ResolveFlame rechecks live victims.
        var foreign = OrdinaryPlayerSession
            ? ActorHost.Combat.ReadGeometry().Where(target => target.Key.SourceGame != ObpSourceGame.Rac1).ToArray()
            : [];
        foreach (var flame in _rac1NativeFlames.ToArray())
        {
            var p = flame.Motion;
            if (!particles.IsLive(p) || p.Remaining < 2)
            {
                _actorHost?.Combat.ForgetFlame(p);
                _rac1NativeFlames.Remove(flame);
                continue;
            }
            particles.FlameSecondary(p);
            Vector3 point = FromEffectNative(p.Position);
            float radius = p.Size * .5f;
            if (OrdinaryPlayerSession)
            {
                // Same native R&C1 travelling flame particle, explicitly admitted
                // to foreign source-owned actor receivers. No second projectile AI.
                foreach (var intent in ActorHost.Combat.ResolveFlame(
                    p, _rac1WeaponWorldTick, point, radius,
                    (float)Rac1WeaponProfiles.PyrocitorDamage, Rac1WeaponProfiles.PyrocitorFlags,
                    foreign))
                    CaptureReplay.RecordGameplayFact("damage", new { Intent = intent, Accepted = true });
            }
            foreach (var (victim, bounds) in EquipmentGeometry)
            {
                var key = new Rac1MobyRuntimeKey(victim.Source.NativeClassId, victim.Source.InstanceIndex);
                if (!Rac1World.Enemies.IsRac1GameplayActive(victim) || flame.Victims.Contains(key) || _rac1FlameHitTicks.GetValueOrDefault(key) > _rac1WeaponWorldTick) continue;
                if (!bounds.Grow(radius).HasPoint(point) || !BombHasLineOfSight(point, victim.Root.GlobalPosition + Vector3.Up * .4f)) continue;
                if (ApplyRac1AdditionalDamage(victim, Rac1WeaponProfiles.DirectDamage(Rac1WeaponId.Pyrocitor, key,
                    Rac1WeaponProfiles.PyrocitorDamage, Rac1WeaponProfiles.PyrocitorFlags)))
                {
                    // Victim repetition remains the existing bounded damage
                    // adapter; it is separate from the recovered particle life.
                    flame.Victims.Add(key); _rac1FlameHitTicks[key] = _rac1WeaponWorldTick + Rac1WeaponReconstructionPolicy.FlameVictimRepeatTicks;
                }
            }
        }
    }

    private Vector3? EquipmentGround(Vector3 point, float up, float down)
    {
        if (_player is null) return null;
        var query = PhysicsRayQueryParameters3D.Create(point + Vector3.Up * up, point - Vector3.Up * down);
        query.Exclude = [_player.GetRid()];
        foreach (var bot in _rac1DoomAgents) query.Exclude.Add(bot.Body.GetRid());
        var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.TryGetValue("position", out var position) && hit["normal"].AsVector3().Y > .55f ? position.AsVector3() : null;
    }

    private void SpawnRac1DoomAgent(HostedEquipmentShot canister, int index)
    {
        var body = new CharacterBody3D
        {
            Name = "Rac1DoomAgent_Class186",
            CollisionLayer = 0,
            CollisionMask = 1,
            FloorSnapLength = .2f,
            FloorMaxAngle = Mathf.DegToRad(50),
            SafeMargin = .005f
        };
        body.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = .22f }, Position = Vector3.Up * .22f });
        _sceneResult!.Root.AddChild(body);
        var view = CreateEquipmentWorldView(186); view.Reparent(body, false); view.Play(0, false, index, 1);
        float direction = index * MathF.Tau / 4;
        Vector3 offset = new(MathF.Cos(direction) * .3f, 0, MathF.Sin(direction) * .3f);
        body.GlobalPosition = EquipmentGround(canister.Position + offset, 1, 2) ?? canister.Position;
        var bot = new HostedDoomAgent(canister.Id * 4 + index + 1, body, view);
        bot.Motion.Wander = ToEffectNative(offset.Normalized());
        _rac1DoomAgents.Add(bot);
    }

    private void StepRac1DoomAgents()
    {
        foreach (var bot in _rac1DoomAgents.ToArray())
        {
            if (!IsInstanceValid(bot.Body)) { _rac1DoomAgents.Remove(bot); continue; }
            var origin = bot.Body.GlobalPosition;
            var target = Rac1World.Enemies.Targets.Where(node => IsRac1WeaponAimTarget(node) && node.Root.GlobalPosition.DistanceSquaredTo(origin) <
                Rac1WeaponReconstructionPolicy.DoomSearchRange * Rac1WeaponReconstructionPolicy.DoomSearchRange &&
                BombHasLineOfSight(origin + Vector3.Up * .3f, node.Root.GlobalPosition + Vector3.Up * .3f))
                .OrderBy(node => node.Root.GlobalPosition.DistanceSquaredTo(origin)).FirstOrDefault();
            System.Numerics.Vector3? targetPoint = target is null ? null : ToEffectNative(target.Root.GlobalPosition);
            Vector3 direction = FromEffectNative(bot.Motion.Direction(ToEffectNative(origin), targetPoint));
            if (bot.Motion.Step(ToEffectNative(origin), targetPoint))
            {
                ResolveEquipmentBlast(Rac1WeaponId.GloveOfDoom, bot.Id, 186, origin + Vector3.Up * .3f, 1, 3, Rac1WeaponProfiles.DoomFlags);
                SpawnRac1WeaponExplosion(Rac1WeaponId.GloveOfDoom, origin + Vector3.Up * .2f);
                FusionWeapons.PlayNativeSound(186, 4, origin);
                bot.Body.QueueFree(); _rac1DoomAgents.Remove(bot); continue;
            }
            // Host ground/obstruction queries prevent the native model from
            // walking through walls or across a gap. Native navigation remains
            // a separate recovery boundary from source clips/damage/emission.
            bool support = EquipmentGround(origin + direction * .45f, .4f, .9f) is not null;
            if (!support) { direction = Vector3.Zero; bot.Motion.Wander = -bot.Motion.Wander; }
            bot.Body.Velocity = direction * Rac1WeaponReconstructionPolicy.DoomRunSpeed + Vector3.Down * (bot.Body.IsOnFloor() ? .1f : 4);
            bot.Body.MoveAndSlide();
            if (direction.LengthSquared() > .01f) bot.View.Rotation = new(0, Mathf.Atan2(direction.Z, -direction.X), 0);
            // Class186 selects sequence7 while chasing and sequence6 near its
            // owner (0x2c0b90/0x2c0cc0). Sequence1 is a leap, not a run loop.
            bot.View.Play(bot.Motion.SourceSequence(direction.LengthSquared() > .01f));
            foreach (int sound in bot.View.Step()) FusionWeapons.PlayNativeSound(186, sound, origin);
            if (target is null)
                for (int collision = 0; collision < bot.Body.GetSlideCollisionCount(); collision++)
                    if (bot.Body.GetSlideCollision(collision).GetNormal().Y < .7f)
                    { bot.Motion.Wander = ToEffectNative(FromEffectNative(bot.Motion.Wander).Rotated(Vector3.Up, .3f)); break; }
        }
    }

    private void ExplodeEquipment(HostedEquipmentShot shot, float radius, double damage, uint flags, int childClass)
    {
        ResolveEquipmentBlast(shot.Weapon, shot.Id, childClass, shot.Position, radius, damage, flags);
        // The native ground/air visual recipe is separate from blast admission.
        SpawnRac1WeaponExplosion(shot.Weapon, shot.Position);
        FusionWeapons.PlayNativeSound(childClass, shot.Weapon == Rac1WeaponId.MineGlove ? 2 : 4, shot.Position);
        RetireEquipmentShot(shot);
    }

    private void ResolveEquipmentBlast(Rac1WeaponId weapon, long id, int nativeClass, Vector3 origin, float radius, double damage, uint flags)
    {
        foreach (var target in EquipmentTargets.ToArray())
        {
            var centre = target.Root.GlobalPosition + Vector3.Up * .4f;
            if (origin.DistanceTo(centre) > radius + .35f || !BombHasLineOfSight(origin + Vector3.Up * .1f, centre)) continue;
            ApplyRac1AdditionalDamage(target, Rac1WeaponProfiles.ChildDamage(weapon, nativeClass, id,
                new(target.Source.NativeClassId, target.Source.InstanceIndex), damage, flags));
        }
    }

    private void RetireEquipmentShot(HostedEquipmentShot shot)
    {
        if (IsInstanceValid(shot.Node)) shot.Node.QueueFree();
        _rac1EquipmentShots.Remove(shot);
    }
}
