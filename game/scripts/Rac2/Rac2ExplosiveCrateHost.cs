using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC2.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Effects;

namespace OBP.Game;

/// <summary>Lease-owned contacts, flashing, native explosion particles and expanding blast queries for RAC2 class 505.</summary>
internal sealed class Rac2ExplosiveCrateHost(RuntimeWorld world, RuntimeWorldScene.Result scene,
    Rac2CrateBoltPorts contacts, Rac2WorldPorts ports, Rac2CrateEffects effects,
    Func<RuntimeWorldScene.DynamicObjectNode, Rac2GameplayDamageEvent, bool> damage,
    Action<RuntimeWorldScene.DynamicObjectNode, Rac2ExplosiveCrateSession> detonate, Func<bool> current) : IDisposable
{
    private sealed record Tint(MeshInstance3D Mesh, Material Original, StandardMaterial3D Flash, Color Color);
    private sealed class Entry(RuntimeWorldScene.DynamicObjectNode node, Rac2ExplosiveCrateSession motion, Tint[] materials)
    {
        internal readonly RuntimeWorldScene.DynamicObjectNode Node = node;
        internal readonly Rac2ExplosiveCrateSession Motion = motion;
        internal readonly Tint[] Materials = materials;
        internal readonly HashSet<WeaponTargetKey> Hits = [];
        internal bool PlayerHit;
    }
    private readonly Dictionary<int, Entry> _entries = [];
    private readonly Dictionary<Rid, Entry> _bodies = [];
    private Rac2CrateExplosion? _explosion;
    private EffectSceneView? _view;
    private double _accumulator;
    private int _damageSequence;
    private bool _disposed;

    internal void Configure(IReadOnlyList<RuntimeWorldScene.DynamicObjectNode> nodes)
    {
        string path = contacts.SourcePath() ?? throw new InvalidDataException("RAC2 explosive source is unavailable.");
        var shortFuse = Rac2ExplosiveCrateSession.CreateFuseSampler(unchecked((uint)contacts.Seed()));
        _explosion = new(path, world.LevelId, unchecked((uint)contacts.Seed()));
        _view = new(scene.Root);
        foreach (var node in nodes)
        {
            var motion = new Rac2ExplosiveCrateSession(node.Source, shortFuse);
            var materials = node.Root.GetChildren().OfType<MeshInstance3D>()
                .Where(mesh => mesh.MaterialOverride is StandardMaterial3D).Select(mesh =>
                {
                    var original = (StandardMaterial3D)mesh.MaterialOverride;
                    var flash = (StandardMaterial3D)original.Duplicate();
                    mesh.MaterialOverride = flash;
                    return new Tint(mesh, original, flash, original.AlbedoColor);
                }).ToArray();
            var entry = new Entry(node, motion, materials);
            _entries.Add(node.Source.InstanceIndex, entry);
            foreach (var body in node.Root.GetChildren().OfType<StaticBody3D>()) _bodies.Add(body.GetRid(), entry);
        }
        GD.Print($"[rac2-explosives] registered={_entries.Count} native class505, flash-bank=LEVEL{world.LevelId}");
    }

    internal int NativeState(RuntimeWorldScene.DynamicObjectNode node) =>
        _entries.TryGetValue(node.Source.InstanceIndex, out var entry) ? entry.Motion.NativeState : 0xfd;

    internal bool Apply(RuntimeWorldScene.DynamicObjectNode node, Rac2CollisionDamage hit) =>
        !_disposed && current() && _entries.TryGetValue(node.Source.InstanceIndex, out var entry) &&
        ReferenceEquals(node, entry.Node) && entry.Motion.ApplyDamage(hit);

    internal void Tick(double delta)
    {
        if (_disposed || !current() || contacts.Player() is not { } player || !GodotObject.IsInstanceValid(player)) return;
        _accumulator = Math.Min(.2, _accumulator + Math.Clamp(delta, 0, .25));
        int ticks = (int)Math.Floor(_accumulator * 60);
        _accumulator -= ticks / 60d;
        for (int tick = 0; tick < ticks; tick++)
        {
            for (int i = 0; i < player.GetSlideCollisionCount(); i++)
            {
                var contact = player.GetSlideCollision(i);
                if (contact.GetCollider() is StaticBody3D body && _bodies.TryGetValue(body.GetRid(), out var entry))
                    entry.Motion.Touch(undersideBreak: contact.GetNormal().Y < -.5f);
            }
            _explosion?.Step();
            foreach (var entry in _entries.Values.ToArray())
            {
                if (_disposed || !current()) return;
                if (entry.Motion.Terminal || !GodotObject.IsInstanceValid(entry.Node.Root)) continue;
                var frame = entry.Motion.Step();
                var flash = Rac2ExplosiveCrateSession.FlashTint;
                foreach (var tint in entry.Materials)
                    tint.Flash.AlbedoColor = frame.FlashRed ? tint.Color * new Color(flash.X, flash.Y, flash.Z) : tint.Color;
                if (frame.WarningSound) effects.PlayCrateCue(entry.Node, 1);
                if (frame.Detonated)
                {
                    detonate(entry.Node, entry.Motion);
                    var camera = scene.Root.GetViewport().GetCamera3D()
                        ?? throw new InvalidOperationException("RAC2 explosion emission requires the active player camera.");
                    _explosion!.Emit(ToNative(BurstCentre(entry.Node)), entry.Node.Source.InstanceIndex,
                        ToNative(camera.GlobalPosition), ToNative(player.GlobalPosition));
                }
                if (frame.MobyRadius > 0 || frame.PlayerRadius > 0) Blast(entry, frame, player);
                if (frame.Terminal) entry.Node.ApplyState(entry.Node.State.WithPresence(RuntimeEntityPresence.Inactive));
            }
        }
    }

    private void Blast(Entry entry, Rac2ExplosiveCrateFrame frame, PlayerHost player)
    {
        Vector3 origin = BurstCentre(entry.Node);
        var excluded = new global::Godot.Collections.Array<Rid> { player.GetRid() };
        contacts.OcclusionExclusions(excluded);
        bool Visible(Vector3 point)
        {
            var ray = PhysicsRayQueryParameters3D.Create(origin, point);
            ray.Exclude = excluded;
            return player.GetWorld3D().DirectSpaceState.IntersectRay(ray).Count == 0;
        }
        var playerPoint = player.GlobalPosition + Vector3.Up * .5f;
        if (!entry.PlayerHit && contacts.HasNativePlayer() && ports.Alive() &&
            origin.DistanceSquaredTo(playerPoint) <= frame.PlayerRadius * frame.PlayerRadius && Visible(playerPoint))
            entry.PlayerHit = ports.ExplosivePlayerDamage(entry.Node, ++_damageSequence,
                Rac2ExplosiveCrateSession.PlayerDamage, Rac2ExplosiveCrateSession.PlayerDamageFlags);

        if (_disposed || !current()) return;

        foreach (var target in ports.Actors().Combat.ReadGeometry().ToArray())
        {
            if (target.Key.SourceGame != ObpSourceGame.Rac2 || ReferenceEquals(target.Node, entry.Node) || entry.Hits.Contains(target.Key)) continue;
            Vector3 nearest = new(Mathf.Clamp(origin.X, target.Bounds.Position.X, target.Bounds.End.X),
                Mathf.Clamp(origin.Y, target.Bounds.Position.Y, target.Bounds.End.Y), Mathf.Clamp(origin.Z, target.Bounds.Position.Z, target.Bounds.End.Z));
            if (origin.DistanceSquaredTo(nearest) > frame.MobyRadius * frame.MobyRadius || !Visible(nearest)) continue;
            var hit = new Rac2GameplayDamageEvent(Rac2GameplayEntityRef.Moby(entry.Node.Source), Rac2GameplayEntityRef.Moby(target.Node.Source),
                new(Rac2ExplosiveCrateSession.MobyDamageFlags, 0, 0, 0, Rac2ExplosiveCrateSession.MobyDamage, 0));
            if (damage(target.Node, hit)) entry.Hits.Add(target.Key);
        }
    }

    internal void Present()
    {
        if (_disposed || !current() || _view is null || _explosion is null || scene.Root.GetViewport().GetCamera3D() is not { } camera) return;
        var basis = camera.GlobalBasis.Orthonormalized();
        var projection = camera.GetCameraProjection();
        float tanY = 1 / MathF.Abs(projection.Y.Y), tanX = 1 / MathF.Abs(projection.X.X);
        float aspect = camera.GetViewport().GetVisibleRect().Size.Aspect();
        var view = new RuntimeEffectView(WorldEffectHost.Runtime(camera.GlobalPosition), WorldEffectHost.Runtime(-basis.Z),
            WorldEffectHost.Runtime(basis.X), WorldEffectHost.Runtime(basis.Y), tanX, tanY, 1 / tanY, aspect);
        _view.Render([_explosion.Capture(view)], _explosion.Textures, aspect);
    }
    private static System.Numerics.Vector3 ToNative(Vector3 value) => new(-value.X, value.Z, value.Y);
    private static Vector3 BurstCentre(RuntimeWorldScene.DynamicObjectNode node) =>
        node.Root.GlobalPosition + node.Root.GlobalBasis.Y.Normalized() * Rac2ExplosiveCrateSession.BurstCentreOffset;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var tint in _entries.Values.SelectMany(entry => entry.Materials))
        {
            if (GodotObject.IsInstanceValid(tint.Mesh)) tint.Mesh.MaterialOverride = tint.Original;
            tint.Flash.Dispose();
        }
        _entries.Clear(); _bodies.Clear();
        _explosion?.Clear(); _view?.Dispose();
        _explosion = null; _view = null;
    }
}
