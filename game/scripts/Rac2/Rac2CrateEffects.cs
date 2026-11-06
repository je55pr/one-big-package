using Godot;
using OBP.Godot;
using OBP.RAC2.Audio;
using OBP.RAC2.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Audio;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>One world lease's original crate sounds, falling supports and detachable model pieces.</summary>
internal sealed class Rac2CrateEffects(RuntimeWorld world, RuntimeWorldScene.Result scene, Rac2CrateBoltPorts ports,
    Func<bool> current) : IDisposable
{
    private sealed record Falling(RuntimeWorldScene.DynamicObjectNode Node, StaticBody3D Body, Rac2CrateFallSession Motion);
    private sealed record Fragment(Node3D Node, Rac2CrateFragment Motion, Quaternion InitialRotation);
    private readonly List<Falling> _crates = [];
    private readonly Dictionary<Rid, Falling> _supports = [];
    private readonly List<Fragment> _fragments = [];
    private WorldMaterialFactory? _materials;
    private Rac2CrateBreakBudget _budget = new();
    private Rac2CrateAssets.Catalogue? _assets;
    private Rac2CrateAudio.Catalogue? _audio;
    private Random? _random;
    private double _accumulator;
    private long _tick, _lastBreakSound = -2;
    private bool _disposed;
    internal sealed record BreakPlan(int Mask, Rac2CrateBreakBudget Budget);

    internal void Configure(IEnumerable<RuntimeWorldScene.DynamicObjectNode> nodes)
    {
        string path = ports.SourcePath() ?? throw new InvalidDataException("Original R&C2 crate effects source is unavailable.");
        _assets = Rac2CrateAssets.Load(path, world.LevelId);
        _audio = Rac2CrateAudio.Load(path, world.LevelId);
        _random = new(ports.Seed());
        _materials = new WorldMaterialFactory(world with { Textures = _assets.Textures });
        foreach (var node in nodes)
        {
            if (!_assets.LitInstances.ContainsKey(node.Source.InstanceIndex))
                throw new InvalidDataException($"Crate {node.Source.InstanceIndex} has no original instance lighting.");
            var body = node.Root.GetChildren().OfType<StaticBody3D>().Single();
            var falling = new Falling(node, body, new(node.Root.GlobalPosition.Y));
            _crates.Add(falling);
            _supports.Add(body.GetRid(), falling);
        }
    }

    // Host pool availability is explicit; it does not claim the retail global allocator's history.
    internal BreakPlan PlanBreak()
    {
        var budget = _budget.Copy();
        return new(budget.Begin(_tick, Math.Max(0, 256 - _fragments.Count)), budget);
    }

    internal void Break(RuntimeWorldScene.DynamicObjectNode crate, BreakPlan plan)
    {
        if (_disposed || !current() || _assets is null) return;
        _budget = plan.Budget;
        if (_tick - _lastBreakSound >= 2)
        {
            Play(500, crate.Root.GlobalPosition);
            _lastBreakSound = _tick;
        }
        foreach (var part in _assets.LitInstances[crate.Source.InstanceIndex].Values.Where(part => (plan.Mask & 1 << part.Index) != 0))
        {
            var pivot = new Vector3(-part.Pivot.X, part.Pivot.Z, part.Pivot.Y);
            Vector3 position = crate.Root.GlobalTransform * pivot;
            var motion = new Rac2CrateFragment(part.Index, ToNative(position), Sample);
            var node = new Node3D { Name = $"Rac2CrateFragment_{crate.Source.InstanceIndex}_{part.Index}" };
            foreach (var surface in part.Surfaces)
                node.AddChild(new MeshInstance3D
                {
                    Mesh = Mesh(surface),
                    MaterialOverride = _materials!.Instanced("moby", surface.TextureId, true, surface.MaterialPresentation),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
                });
            scene.Root.AddChild(node);
            node.GlobalPosition = position;
            node.Quaternion = crate.Root.GlobalBasis.GetRotationQuaternion();
            node.Scale = crate.Root.GlobalBasis.Scale;
            _fragments.Add(new(node, motion, node.Quaternion));
        }
    }

    internal void PlayPickup(int nativeClass)
    {
        if (_disposed || !current()) return;
        Play(nativeClass, null);
    }

    private void Play(int nativeClass, Vector3? position)
    {
        if (_audio is not { } bank || !bank.Cues.TryGetValue(nativeClass, out var cue)) return;
        var clip = cue.Variants[_random!.Next(cue.Variants.Count)];
        RuntimeAudioPosition? point = position is { } p ? new(-p.X, p.Y, p.Z) : null;
        if (ports.PlayEffect(new(clip, RuntimeAudioCategory.SoundEffect, gain: cue.Gain, position: point)))
        {
            GD.Print($"[rac2-crate-audio] class={nativeClass} sound=0 bank={cue.BankSound} gain={cue.Gain:0.###}");
        }
    }

    internal void Tick(double delta)
    {
        if (_disposed || !current() || ports.Player() is not { } player || !GodotObject.IsInstanceValid(player)) return;
        _accumulator = Math.Min(.2, _accumulator + Math.Clamp(delta, 0, .25));
        int count = (int)Math.Floor(_accumulator * 60);
        _accumulator -= count / 60d;
        var space = player.GetWorld3D().DirectSpaceState;
        var excluded = new global::Godot.Collections.Array<Rid> { player.GetRid() };
        ports.ActorExclusions(excluded);
        for (int tick = 0; tick < count; tick++)
        {
            _tick++;
            foreach (var crate in _crates.OrderBy(crate => crate.Motion.Height)) StepCrate(crate, space, excluded);
            StepFragments(space, excluded);
        }
    }

    private void StepCrate(Falling crate, PhysicsDirectSpaceState3D space, global::Godot.Collections.Array<Rid> excluded)
    {
        if (crate.Node.State.Presentation.Presence != RuntimeEntityPresence.Active) return;
        var position = crate.Node.Root.GlobalPosition;
        var query = PhysicsRayQueryParameters3D.Create(position + Vector3.Up * .75f,
            position - Vector3.Up * (Rac2CrateFallSession.MaximumFallPerTick + .02f));
        var exclusions = new global::Godot.Collections.Array<Rid>(excluded) { crate.Body.GetRid() };
        query.Exclude = exclusions;
        var hit = space.IntersectRay(query);
        float? support = null;
        if (hit.TryGetValue("normal", out var normal) && normal.AsVector3().Y > .70710677f && hit.TryGetValue("position", out var point))
        {
            support = point.AsVector3().Y;
            if (hit.TryGetValue("rid", out var rid) && _supports.TryGetValue(rid.AsRid(), out var lower))
                support = lower.Motion.Height + 1; // 0x392C08..0x392C18: native crate spacing.
        }
        crate.Motion.Step(support);
        if (MathF.Abs(crate.Motion.Height - position.Y) < 1e-6f) return;
        double[] matrix = (double[])crate.Node.State.Presentation.Transform.Matrix.Clone();
        matrix[13] = crate.Motion.Height;
        crate.Node.ApplyState(crate.Node.State.WithTransform(new(matrix)));
    }

    private void StepFragments(PhysicsDirectSpaceState3D space, global::Godot.Collections.Array<Rid> excluded)
    {
        for (int index = _fragments.Count - 1; index >= 0; index--)
        {
            var fragment = _fragments[index];
            var before = FromNative(fragment.Motion.Position);
            var next = FromNative(fragment.Motion.Position + fragment.Motion.Velocity);
            var query = PhysicsRayQueryParameters3D.Create(before + Vector3.Up * .03f, next - Vector3.Up * .03f);
            query.Exclude = excluded;
            var hit = space.IntersectRay(query);
            Rac2BoltContact? contact = hit.TryGetValue("position", out var point) && hit.TryGetValue("normal", out var normal)
                ? new(ToNative(point.AsVector3()) + ToNative(normal.AsVector3()) * .03f, ToNative(normal.AsVector3())) : null;
            fragment.Motion.Step(contact);
            fragment.Node.GlobalPosition = FromNative(fragment.Motion.Position);
            var q = fragment.Motion.Rotation;
            fragment.Node.Quaternion = new Quaternion(-q.X, q.Z, q.Y, q.W) * fragment.InitialRotation;
            foreach (var mesh in fragment.Node.GetChildren().OfType<MeshInstance3D>()) mesh.Transparency = 1 - fragment.Motion.Alpha;
            if (!fragment.Motion.Expired) continue;
            fragment.Node.QueueFree();
            _fragments.RemoveAt(index);
        }
    }

    private static ArrayMesh Mesh(RuntimeObjectMesh surface)
    {
        var vertices = Enumerable.Range(0, surface.Positions.Length / 3)
            .Select(i => RuntimeWorldScene.ToScene(surface.Positions[i * 3], surface.Positions[i * 3 + 1], surface.Positions[i * 3 + 2])).ToArray();
        var uvs = Enumerable.Range(0, surface.Uvs.Length / 2).Select(i => new Vector2(surface.Uvs[i * 2], surface.Uvs[i * 2 + 1])).ToArray();
        int[] indices = (int[])surface.Indices.Clone();
        for (int at = 0; at < indices.Length; at += 3) (indices[at + 1], indices[at + 2]) = (indices[at + 2], indices[at + 1]);
        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)global::Godot.Mesh.ArrayType.Max);
        arrays[(int)global::Godot.Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)global::Godot.Mesh.ArrayType.TexUV] = uvs;
        var colors = surface.Colors ?? throw new InvalidDataException("Crate fragment instance lighting is missing.");
        arrays[(int)global::Godot.Mesh.ArrayType.Color] = Enumerable.Range(0, vertices.Length)
            .Select(i => new Color(colors[i * 4], colors[i * 4 + 1], colors[i * 4 + 2], colors[i * 4 + 3])).ToArray();
        arrays[(int)global::Godot.Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(global::Godot.Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
    private float Sample(float min, float max) => min + _random!.NextSingle() * (max - min);
    private static System.Numerics.Vector3 ToNative(Vector3 point) => new(-point.X, point.Z, point.Y);
    private static Vector3 FromNative(System.Numerics.Vector3 point) => new(-point.X, point.Z, point.Y);
    public void Dispose()
    {
        _disposed = true;
        foreach (var fragment in _fragments) if (GodotObject.IsInstanceValid(fragment.Node)) fragment.Node.QueueFree();
        _fragments.Clear(); _crates.Clear(); _supports.Clear();
        _audio = null; _assets = null; _materials = null;
    }
}
