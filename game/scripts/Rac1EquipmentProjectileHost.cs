using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

/// <summary>
/// Borrowed scene/player facts for world-scoped equipment projectile lifetime.
/// Does not own persistent inventory, native damage policy or an extra RNG.
/// </summary>
internal sealed record Rac1EquipmentProjectilePorts(
    Func<PlayerHost?> Player,
    Func<float> AvatarBaseHeight,
    Func<Rac1WeaponId, Vector3> Muzzle,
    Func<Vector3> Forward,
    Func<int, Rac1EquipmentView> CreateView,
    Func<int, Color, float, Node3D> Particle,
    Func<uint, Color> ColorFromProfile,
    Action<Vector3, int, Color, float, int> Flash,
    Action<Rac1WeaponId, Vector3> Explosion,
    Action<int, int, Vector3> PlaySound,
    Func<IEnumerable<RuntimeWorldScene.DynamicObjectNode>> EnemyTargets,
    Func<int, bool> EnemyRosterContains,
    Func<RuntimeWorldScene.DynamicObjectNode, Rac1GameplayDamageEvent, bool> ApplyDamage,
    Func<Node3D> SceneRoot);

internal sealed class Rac1HostedEquipmentShot(Rac1WeaponId weapon, long id, Node3D node, Vector3 position, Vector3 step)
{
    public Rac1WeaponId Weapon { get; } = weapon;
    public long Id { get; } = id;
    public Node3D Node { get; } = node;
    public Vector3 Position = position;
    public Vector3 Step = step;
    public int Age;
    public bool Grounded;
    public float GroundHeight;
    public Rac1DoomCanisterSession? Canister;
    public Rac1ArsenalProjectile? Motion;
    public NVector3? AimPoint;
    public bool Taunted;
}

internal sealed record Rac1HostedDoomAgent(long Id, CharacterBody3D Body, Rac1EquipmentView View)
{
    public Rac1DoomAgentSession Motion { get; } = new();
}

internal sealed record Rac1HostedWeaponMote(Node3D Node, Vector3 Step)
{
    public int Age;
}

/// <summary>
/// One R&C1 world lease owns active Blaster/Suck carriers, thrown glove
/// projectiles, Doom agents, Taunter/Suck motes and flash presentation.
/// Existing native source rules and shared contact/damage authorities stay
/// in their respective sessions; reset and teardown cannot leak Godot nodes.
/// </summary>
internal sealed class Rac1EquipmentProjectileHost(
    Rac1WeaponContactHost _contacts, Rac1EquipmentProjectilePorts ports) : IDisposable
{
    private readonly List<Rac1HostedEquipmentShot> _shots = [];
    private readonly List<Rac1HostedDoomAgent> _doomAgents = [];
    private readonly List<Rac1HostedWeaponMote> _motes = [];
    private readonly List<(Node3D Node, int Age, int Lifetime, float Size)> _flashes = [];
    private bool _disposed;

    internal IReadOnlyList<Rac1HostedEquipmentShot> Shots => _disposed ? [] : _shots;
    internal IReadOnlyList<Rac1HostedDoomAgent> DoomAgents => _disposed ? [] : _doomAgents;
    internal Vector3? MinePosition(long id) =>
        _disposed ? null : _shots.FirstOrDefault(shot => shot.Weapon == Rac1WeaponId.MineGlove && shot.Id == id)?.Position;
    internal Vector3? DoomPosition(long id) =>
        _disposed ? null : _doomAgents.FirstOrDefault(bot => bot.Id == id)?.Body.GlobalPosition;

    internal void AddSuckCarrier(long generation, Node3D visual, Vector3 origin, Vector3 step)
    {
        if (!_disposed) _shots.Add(new(Rac1WeaponId.SuckCannon, generation, visual, origin, step));
    }

    internal void AddMote(Node3D node, Vector3 step)
    {
        if (!_disposed) _motes.Add(new(node, step));
    }

    internal void AddFlash(Node3D node, int ticks, float size)
    {
        if (!_disposed) _flashes.Add((node, 0, ticks, size));
    }

    internal void RetireShotsAgentsMotes()
    {
        foreach (var node in _shots.Select(shot => shot.Node).Concat(_doomAgents.Select(agent => agent.Body))
            .Concat(_motes.Select(mote => mote.Node)))
            if (GodotObject.IsInstanceValid(node)) node.QueueFree();
        _shots.Clear(); _doomAgents.Clear(); _motes.Clear();
    }

    internal void ResetFlashes()
    {
        foreach (var flash in _flashes) if (GodotObject.IsInstanceValid(flash.Node)) flash.Node.QueueFree();
        _flashes.Clear();
    }

    internal void Reset()
    {
        if (_disposed) return;
        RetireShotsAgentsMotes();
        ResetFlashes();
    }

    internal void StepShots()
    {
        if (_disposed) return;
        foreach (var shot in _shots.ToArray())
        {
            if (!GodotObject.IsInstanceValid(shot.Node)) { _shots.Remove(shot); continue; }
            shot.Age++;
            if (shot.Weapon is Rac1WeaponId.Blaster or Rac1WeaponId.SuckCannon)
            {
                Vector3 next = shot.Position + shot.Step;
                if (shot.Motion is { } motion)
                {
                    motion.Step(shot.AimPoint, 1.5f * MathF.PI);
                    next = Rac1LootPresentation.FromEffectNative(motion.Position);
                }
                int lifetime = shot.Weapon == Rac1WeaponId.Blaster ? Rac1WeaponProfiles.BlasterCarrierTicks : Rac1WeaponReconstructionPolicy.SuckProjectileLifetimeTicks;
                if (shot.Age > lifetime)
                {
                    RetireEquipmentShot(shot); continue;
                }
                if (_contacts.Contact(shot.Position, next, shot.Weapon == Rac1WeaponId.Blaster ? .15f : .25f) is { } hit)
                {
                    if (hit.Target is { } victim)
                        ports.ApplyDamage(victim, Rac1WeaponProfiles.DirectDamage(shot.Weapon,
                            new(victim.Source.NativeClassId, victim.Source.InstanceIndex),
                            shot.Weapon == Rac1WeaponId.Blaster ? Rac1WeaponProfiles.BlasterDamage : Rac1WeaponReconstructionPolicy.SuckDamage,
                            shot.Weapon == Rac1WeaponId.Blaster ? Rac1WeaponProfiles.BlasterFlags : Rac1WeaponReconstructionPolicy.SuckDamageFlags));
                    ports.Flash(hit.Point, 27, new(.7f, .8f, 1, 1), .6f, 10);
                    ports.PlaySound(849, 0, hit.Point); RetireEquipmentShot(shot); continue;
                }
                shot.Position = next; shot.Node.GlobalPosition = next;
                if (shot.Weapon == Rac1WeaponId.SuckCannon) shot.Node.RotateY(.25f);
                continue;
            }
            if (shot.Node is Rac1EquipmentView skin)
            {
                if (shot.Weapon == Rac1WeaponId.GloveOfDoom)
                    skin.Scale = Vector3.One * Math.Min(Rac1ArsenalRules.DoomCanisterThrownScale, skin.Scale.X + Rac1ArsenalRules.DoomCanisterGrowth);
                foreach (int sound in skin.Step()) ports.PlaySound(shot.Weapon == Rac1WeaponId.MineGlove ? 74 : 230, sound, shot.Position);
                if (shot.Grounded)
                { shot.Position = new(shot.Position.X, shot.GroundHeight + skin.FootOffset * skin.Scale.Y, shot.Position.Z); shot.Node.GlobalPosition = shot.Position; }
            }
            if (!shot.Grounded)
            {
                Vector3 next = shot.Position + shot.Step;
                shot.Step -= Vector3.Up * OBP.RAC1.Gameplay.Rac1BombProjectileSession.GravityStep;
                if (_contacts.Contact(shot.Position, next, .12f) is { } hit)
                {
                    if (shot.Weapon == Rac1WeaponId.MineGlove && hit.Target is { } directVictim &&
                        ports.EnemyRosterContains(directVictim.Source.InstanceIndex))
                    { shot.Position = hit.Point; ExplodeEquipment(shot, 2, 3, Rac1WeaponProfiles.MineFlags, 74); continue; }
                    shot.Position = hit.Point + Vector3.Up * (((shot.Node as Rac1EquipmentView)?.FootOffset ?? .06f) * shot.Node.Scale.Y);
                    shot.GroundHeight = hit.Point.Y;
                    shot.Grounded = true; shot.Step = Vector3.Zero;
                    if (shot.Node is Rac1EquipmentView mine && shot.Weapon == Rac1WeaponId.MineGlove)
                        mine.Play(1, false, shot.Id, 2);
                    ports.PlaySound(shot.Weapon == Rac1WeaponId.MineGlove ? 74 : 230, 0, hit.Point);
                }
                else { shot.Position = next; shot.Node.RotateY(.15f); }
                shot.Node.GlobalPosition = shot.Position;
            }
            else if (shot.Canister is { } canister)
            {
                if (canister.Step(_doomAgents.Count)) SpawnRac1DoomAgent(shot, canister.Emitted - 1);
                if (canister.Finished) { RetireEquipmentShot(shot); continue; }
            }
            else
            {
                // Grounded mine proximity/seek is an OBP query adapter. Native
                // class74 animation and damage descriptors remain source-owned.
                var enemy = ports.EnemyTargets().Where(_contacts.IsAimTarget)
                    .OrderBy(node => node.Root.GlobalPosition.DistanceSquaredTo(shot.Position)).FirstOrDefault();
                if (enemy is not null && shot.Age > 20)
                {
                    Vector3 goal = enemy.Root.GlobalPosition + Vector3.Up * .2f;
                    float distance = goal.DistanceTo(shot.Position);
                    if (distance < Rac1WeaponReconstructionPolicy.MineDetonationDistance * (shot.Taunted ? 3 : 1) && _contacts.HasLineOfSight(shot.Position + Vector3.Up * .15f, goal))
                    { ExplodeEquipment(shot, 2, 3, Rac1WeaponProfiles.MineFlags, 74); continue; }
                    if (distance < Rac1WeaponReconstructionPolicy.MineSeekRange * (shot.Taunted ? 3 : 1) && _contacts.HasLineOfSight(shot.Position + Vector3.Up * .2f, goal))
                    {
                        Vector3 step = (goal - shot.Position).Slide(Vector3.Up).Normalized() * Rac1WeaponReconstructionPolicy.MineSeekStep;
                        if (Ground(shot.Position + step, .5f, 1.2f) is { } ground)
                        { shot.GroundHeight = ground.Y; shot.Position = ground + Vector3.Up * ((shot.Node as Rac1EquipmentView)?.FootOffset ?? .06f); shot.Node.GlobalPosition = shot.Position; }
                    }
                }
            }
            if (shot.Age > Rac1WeaponReconstructionPolicy.PlantedLifetimeTicks || shot.Position.Y < -100) RetireEquipmentShot(shot);
        }
    }

    internal void StepFlashes()
    {
        if (_disposed) return;
        for (int i = _flashes.Count - 1; i >= 0; i--)
        {
            var item = _flashes[i];
            if (!GodotObject.IsInstanceValid(item.Node) || ++item.Age >= item.Lifetime)
            {
                if (GodotObject.IsInstanceValid(item.Node)) item.Node.QueueFree();
                _flashes.RemoveAt(i); continue;
            }
            item.Node.Scale = Vector3.One * item.Size * (1 - item.Age / (float)item.Lifetime);
            _flashes[i] = item;
        }
    }

    internal void FireBlaster(Rac1AdditionalWeaponSession.Shot shot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Vector3 origin = ports.Muzzle(shot.Weapon);
        Vector3 end = origin + (_contacts.Aim(origin, Rac1WeaponReconstructionPolicy.BlasterAimRange, .96f) - origin).Normalized() * Rac1WeaponReconstructionPolicy.BlasterAimRange;
        var node = ports.Particle(27, ports.ColorFromProfile(Rac1WeaponProfiles.BlasterImpactColor), .18f);
        node.Name = "Rac1Blaster_Class305_Type27";
        ports.SceneRoot().AddChild(node);
        node.GlobalPosition = origin;
        _shots.Add(new(shot.Weapon, shot.Generation, node, origin, (end - origin).Normalized() * Rac1WeaponProfiles.BlasterSpeed)
        {
            Motion = new(Rac1LootPresentation.ToEffectNative(origin), Rac1LootPresentation.ToEffectNative((end - origin).Normalized()), 40, Rac1WeaponProfiles.BlasterCarrierTicks),
            AimPoint = Rac1LootPresentation.ToEffectNative(_contacts.Aim(origin, Rac1WeaponReconstructionPolicy.BlasterAimRange, .96f)),
        });
        ports.Flash(origin, 27, ports.ColorFromProfile(Rac1WeaponProfiles.BlasterMuzzleColor), .35f, 4);
    }

    internal void Throw(Rac1AdditionalWeaponSession.Shot shot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ports.PlaySound(0, 26, ports.Player()!.GlobalPosition); // hero_sound_start(0x1a,0), shared glove throw.
        float yaw = (float)ports.Player()!.Rac1CurrentYaw;
        NVector3 body = Rac1LootPresentation.ToEffectNative(ports.Player()!.GlobalPosition) + new NVector3(0, 0, (float)-(ports.AvatarBaseHeight()));
        var origin = Rac1BombProjectileSession.LaunchOrigin(body, yaw);
        var target = Rac1LootPresentation.ToEffectNative(_contacts.Aim(Rac1LootPresentation.FromEffectNative(origin), 8.5f, .94f));
        var velocity = Rac1BombProjectileSession.LaunchVelocity(origin, target, yaw);
        // Shared glove-origin family is recovered; this flight adapter currently
        // uses the recovered Bomb arc pending the complete class74/230 integrator.
        int nativeClass = shot.Weapon == Rac1WeaponId.MineGlove ? 74 : 230;
        var view = ports.CreateView(nativeClass);
        view.Play(0); view.GlobalPosition = Rac1LootPresentation.FromEffectNative(origin);
        if (nativeClass == 230) view.Scale = Vector3.One * Rac1ArsenalRules.DoomCanisterHeldScale;
        var flying = new Rac1HostedEquipmentShot(shot.Weapon, shot.Generation, view, view.GlobalPosition,
            new(-velocity.X, velocity.Z, velocity.Y))
        { Canister = nativeClass == 230 ? new() : null };
        _shots.Add(flying);
        var start = Rac1LootPresentation.FromEffectNative(new(body.X, body.Y, origin.Z));
        if (_contacts.Contact(start, view.GlobalPosition, .12f) is { } obstruction)
        { flying.Position = obstruction.Point; flying.GroundHeight = obstruction.Point.Y; flying.Grounded = true; flying.Step = Vector3.Zero; }
        if (nativeClass == 74)
        {
            var mines = _shots.Where(item => item.Weapon == Rac1WeaponId.MineGlove).ToArray();
            if (mines.Length > 8) ExplodeEquipment(mines[0], 2, Rac1WeaponProfiles.MineDamage, Rac1WeaponProfiles.MineFlags, 74);
        }
    }

    internal void StepMotes()
    {
        if (_disposed) return;
        foreach (var mote in _motes.ToArray())
        {
            if (!GodotObject.IsInstanceValid(mote.Node)) { _motes.Remove(mote); continue; }
            var from = mote.Node.GlobalPosition; var next = from + mote.Step;
            // The remaining Taunter/Suck presentation motes are not the native
            // flame pool and cannot submit Pyrocitor damage.
            if (++mote.Age > Rac1WeaponReconstructionPolicy.FlameLifetimeTicks || !_contacts.HasLineOfSight(from, next))
            { mote.Node.QueueFree(); _motes.Remove(mote); continue; }
            mote.Node.GlobalPosition = next;
            float radius = .12f + mote.Age * .025f;
            mote.Node.Scale = Vector3.One * radius * 2;
        }
    }

    internal Vector3? Ground(Vector3 point, float up, float down)
    {
        if (_disposed || ports.Player() is null) return null;
        var query = PhysicsRayQueryParameters3D.Create(point + Vector3.Up * up, point - Vector3.Up * down);
        query.Exclude = [ports.Player()!.GetRid()];
        foreach (var bot in _doomAgents) query.Exclude.Add(bot.Body.GetRid());
        var hit = ports.Player()!.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.TryGetValue("position", out var position) && hit["normal"].AsVector3().Y > .55f ? position.AsVector3() : null;
    }

    private void SpawnRac1DoomAgent(Rac1HostedEquipmentShot canister, int index)
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
        ports.SceneRoot().AddChild(body);
        var view = ports.CreateView(186); view.Reparent(body, false); view.Play(0, false, index, 1);
        float direction = index * MathF.Tau / 4;
        Vector3 offset = new(MathF.Cos(direction) * .3f, 0, MathF.Sin(direction) * .3f);
        body.GlobalPosition = Ground(canister.Position + offset, 1, 2) ?? canister.Position;
        var bot = new Rac1HostedDoomAgent(canister.Id * 4 + index + 1, body, view);
        bot.Motion.Wander = Rac1LootPresentation.ToEffectNative(offset.Normalized());
        _doomAgents.Add(bot);
    }

    internal void StepDoomAgents()
    {
        if (_disposed) return;
        foreach (var bot in _doomAgents.ToArray())
        {
            if (!GodotObject.IsInstanceValid(bot.Body)) { _doomAgents.Remove(bot); continue; }
            var origin = bot.Body.GlobalPosition;
            var target = ports.EnemyTargets().Where(node => _contacts.IsAimTarget(node) && node.Root.GlobalPosition.DistanceSquaredTo(origin) <
                Rac1WeaponReconstructionPolicy.DoomSearchRange * Rac1WeaponReconstructionPolicy.DoomSearchRange &&
                _contacts.HasLineOfSight(origin + Vector3.Up * .3f, node.Root.GlobalPosition + Vector3.Up * .3f))
                .OrderBy(node => node.Root.GlobalPosition.DistanceSquaredTo(origin)).FirstOrDefault();
            System.Numerics.Vector3? targetPoint = target is null ? null : Rac1LootPresentation.ToEffectNative(target.Root.GlobalPosition);
            Vector3 direction = Rac1LootPresentation.FromEffectNative(bot.Motion.Direction(Rac1LootPresentation.ToEffectNative(origin), targetPoint));
            if (bot.Motion.Step(Rac1LootPresentation.ToEffectNative(origin), targetPoint))
            {
                ResolveEquipmentBlast(Rac1WeaponId.GloveOfDoom, bot.Id, 186, origin + Vector3.Up * .3f, 1, 3, Rac1WeaponProfiles.DoomFlags);
                ports.Explosion(Rac1WeaponId.GloveOfDoom, origin + Vector3.Up * .2f);
                ports.PlaySound(186, 4, origin);
                bot.Body.QueueFree(); _doomAgents.Remove(bot); continue;
            }
            // Host ground/obstruction queries prevent the native model from
            // walking through walls or across a gap. Native navigation remains
            // a separate recovery boundary from source clips/damage/emission.
            bool support = Ground(origin + direction * .45f, .4f, .9f) is not null;
            if (!support) { direction = Vector3.Zero; bot.Motion.Wander = -bot.Motion.Wander; }
            bot.Body.Velocity = direction * Rac1WeaponReconstructionPolicy.DoomRunSpeed + Vector3.Down * (bot.Body.IsOnFloor() ? .1f : 4);
            bot.Body.MoveAndSlide();
            if (direction.LengthSquared() > .01f) bot.View.Rotation = new(0, Mathf.Atan2(direction.Z, -direction.X), 0);
            // Class186 selects sequence7 while chasing and sequence6 near its
            // owner (0x2c0b90/0x2c0cc0). Sequence1 is a leap, not a run loop.
            bot.View.Play(bot.Motion.SourceSequence(direction.LengthSquared() > .01f));
            foreach (int sound in bot.View.Step()) ports.PlaySound(186, sound, origin);
            if (target is null)
                for (int collision = 0; collision < bot.Body.GetSlideCollisionCount(); collision++)
                    if (bot.Body.GetSlideCollision(collision).GetNormal().Y < .7f)
                    { bot.Motion.Wander = Rac1LootPresentation.ToEffectNative(Rac1LootPresentation.FromEffectNative(bot.Motion.Wander).Rotated(Vector3.Up, .3f)); break; }
        }
    }

    private void ExplodeEquipment(Rac1HostedEquipmentShot shot, float radius, double damage, uint flags, int childClass)
    {
        ResolveEquipmentBlast(shot.Weapon, shot.Id, childClass, shot.Position, radius, damage, flags);
        // The native ground/air visual recipe is separate from blast admission.
        ports.Explosion(shot.Weapon, shot.Position);
        ports.PlaySound(childClass, shot.Weapon == Rac1WeaponId.MineGlove ? 2 : 4, shot.Position);
        RetireEquipmentShot(shot);
    }

    private void ResolveEquipmentBlast(Rac1WeaponId weapon, long id, int nativeClass, Vector3 origin, float radius, double damage, uint flags)
    {
        foreach (var target in _contacts.Targets.ToArray())
        {
            var centre = target.Root.GlobalPosition + Vector3.Up * .4f;
            if (origin.DistanceTo(centre) > radius + .35f || !_contacts.HasLineOfSight(origin + Vector3.Up * .1f, centre)) continue;
            ports.ApplyDamage(target, Rac1WeaponProfiles.ChildDamage(weapon, nativeClass, id,
                new(target.Source.NativeClassId, target.Source.InstanceIndex), damage, flags));
        }
    }

    private void RetireEquipmentShot(Rac1HostedEquipmentShot shot)
    {
        if (GodotObject.IsInstanceValid(shot.Node)) shot.Node.QueueFree();
        _shots.Remove(shot);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Reset();
        _disposed = true;
    }
}
