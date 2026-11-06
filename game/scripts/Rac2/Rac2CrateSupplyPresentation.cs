using Godot;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC2.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

internal sealed record Rac2CrateSupplyPorts(
    Func<(int Current, int Capacity)> Health,
    Func<int, int> Heal,
    Func<int, IReadOnlyList<PlayerAmmoSupply>> Ammo,
    Func<SourceWeaponKey, int, int> GrantAmmo,
    Func<string?> NanotechPresentationSource);

/// <summary>Lease-owned supply pickups, using original ammo geometry and shared crate effect ownership.</summary>
internal sealed class Rac2CrateSupplyPresentation(RuntimeWorld world, RuntimeWorldScene.Result scene,
    Rac2CrateBoltPorts ports, Rac2CrateSupplyPorts supply, Rac2CrateEffects effects, Func<bool> current) : IDisposable
{
    private sealed record Ammo(Node3D Node, Rac2AmmoPickup Motion);
    private sealed record Cloud(Rac2NanotechCloud Motion, Rac1NanotechCluster Cluster);
    private readonly List<Ammo> _ammo = [];
    private readonly List<Cloud> _clouds = [];
    private readonly List<(ArrayMesh Mesh, Material Material)> _ammoModel = [];
    private Rac2SupplyAssets.Catalogue? _assets;
    private WorldMaterialFactory? _materials;
    private Random? _random;
    private Rac1NanotechSpherePresentation? _orbs;
    private double _accumulator;
    private float _ammoGroundOffset;
    private bool _disposed;

    internal void Prepare(IEnumerable<RuntimeWorldScene.DynamicObjectNode> crates)
    {
        var path = ports.SourcePath() ?? throw new InvalidDataException("R&C2 supply source is unavailable.");
        _assets = Rac2SupplyAssets.Load(path, world.LevelId);
        // Keep the original model's bottom above the host contact plane.
        _ammoGroundOffset = Math.Max(0, -(float)_assets.Surfaces.SelectMany(surface =>
            Enumerable.Range(0, surface.Positions.Length / 3).Select(index => surface.Positions[index * 3 + 1])).Min());
        _random = new(ports.Seed());
        _materials = new(world with { Textures = _assets.Textures });
        foreach (var surface in _assets.Surfaces)
        {
            var arrays = new global::Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = Enumerable.Range(0, surface.Positions.Length / 3)
                .Select(i => RuntimeWorldScene.ToScene(surface.Positions[i * 3], surface.Positions[i * 3 + 1], surface.Positions[i * 3 + 2])).ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = Enumerable.Range(0, surface.Uvs.Length / 2)
                .Select(i => new Vector2(surface.Uvs[i * 2], surface.Uvs[i * 2 + 1])).ToArray();
            var indices = (int[])surface.Indices.Clone();
            for (int i = 0; i < indices.Length; i += 3) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
            arrays[(int)Mesh.ArrayType.Index] = indices;
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            _ammoModel.Add((mesh, _materials.Instanced("moby", surface.TextureId, false, surface.MaterialPresentation)));
        }
        var nanotech = crates.Where(crate => crate.Source.NativeClassId == Rac2CrateSupply.NanotechClass).ToArray();
        if (nanotech.Length > 0)
            _orbs = new(scene, supply.NanotechPresentationSource()
                ?? throw new InvalidDataException("The selected RAC1 nanotech sphere presentation source is unavailable."), ports.Seed(), nanotech);
    }

    internal void Spawn(RuntimeWorldScene.DynamicObjectNode crate)
    {
        if (_disposed || !current() || _assets is null) return;
        var origin = crate.Root.GlobalPosition + Vector3.Up * .5f;
        if (crate.Source.NativeClassId == Rac2CrateSupply.NanotechClass)
        {
            var cluster = _orbs?.Break(crate) ?? throw new InvalidOperationException("Nanotech sphere presentation was not prepared.");
            _clouds.Add(new(new(ToNative(origin)), cluster));
            GD.Print($"[rac2-supply] crate={crate.Source.InstanceIndex} nanotech cloud=1 presentation=rac1-sphere");
            return;
        }
        int count = Rac2CrateSupply.AmmoPieceCount(_random!.Next(5));
        for (int i = 0; i < count; i++)
        {
            var node = new Node3D { Name = $"Rac2Ammo_{crate.Source.InstanceIndex}_{i}" };
            foreach (var model in _ammoModel) node.AddChild(new MeshInstance3D { Mesh = model.Mesh, MaterialOverride = model.Material });
            scene.Root.AddChild(node);
            node.GlobalPosition = origin;
            _ammo.Add(new(node, new(ToNative(origin), Sample, PlayerAmmoSupplyPolicy.DropGraceTicks)));
        }
        GD.Print($"[rac2-supply] crate={crate.Source.InstanceIndex} ammo pieces={count} recipient=pending-collection");
    }

    internal void Tick(double delta)
    {
        if (_disposed || !current() || ports.Player() is not { } player || !GodotObject.IsInstanceValid(player)) return;
        _accumulator = Math.Min(.2, _accumulator + Math.Clamp(delta, 0, .25));
        int ticks = (int)Math.Floor(_accumulator * 60);
        _accumulator -= ticks / 60d;
        var space = player.GetWorld3D().DirectSpaceState;
        var excluded = new global::Godot.Collections.Array<Rid> { player.GetRid() };
        ports.ActorExclusions(excluded);
        for (int tick = 0; tick < ticks; tick++)
        {
            var root = ToNative(player.GlobalPosition);
            var socket = ToNative(player.GlobalPosition + Vector3.Up * .7f);
            _orbs?.BeginTick(root, socket);
            foreach (var pickup in _ammo.ToArray())
            {
                if (ports.Alive() && pickup.Motion.CanCollect(root, PlayerAmmoSupplyPolicy.PickupRadius, PlayerAmmoSupplyPolicy.PickupHeight))
                {
                    var query = PhysicsRayQueryParameters3D.Create(FromNative(pickup.Motion.Position) + Vector3.Up * .1f, FromNative(socket));
                    query.Exclude = excluded;
                    if (space.IntersectRay(query).Count == 0 &&
                        PlayerAmmoSupplyPolicy.Select(supply.Ammo(_assets!.Lancer.Amount), _random!.Next) is { } selected)
                    {
                        int granted = supply.GrantAmmo(selected.Weapon, selected.Amount);
                        if (granted > 0)
                        {
                            pickup.Motion.Claim();
                            effects.PlayPickup(Rac2CrateSupply.AmmoPickupClass);
                            ports.RefreshHud();
                            GD.Print($"[rac2-supply] ammo collected recipient={selected.Weapon} delta={granted}");
                        }
                    }
                }
                pickup.Motion.Step(socket, (from, to) =>
                {
                    var bottom = Vector3.Up * _ammoGroundOffset;
                    var query = PhysicsRayQueryParameters3D.Create(FromNative(from) - bottom + Vector3.Up * .1f, FromNative(to) - bottom);
                    query.Exclude = excluded;
                    var hit = space.IntersectRay(query);
                    return hit.TryGetValue("position", out var point) && hit.TryGetValue("normal", out var normal) && normal.AsVector3().Y > .70710677f
                        ? ToNative(point.AsVector3() + bottom) : null;
                });
                pickup.Node.GlobalPosition = FromNative(pickup.Motion.Position);
                pickup.Node.Rotation = new(0, pickup.Motion.Spin, 0);
                if (!pickup.Motion.Arrived) continue;
                pickup.Node.QueueFree();
                _ammo.Remove(pickup);
            }
            foreach (var cloud in _clouds.ToArray())
            {
                var health = supply.Health();
                int pending = _clouds.Count(other => other != cloud && other.Motion.Attracting && !other.Motion.Collected);
                var query = PhysicsRayQueryParameters3D.Create(FromNative(cloud.Motion.Origin), FromNative(socket));
                query.Exclude = excluded;
                bool visible = cloud.Motion.Attracting || space.IntersectRay(query).Count == 0;
                if (cloud.Motion.TryAttract(root, ports.Alive() ? health.Current : 0, health.Capacity, pending, visible))
                    cloud.Cluster.Launch(socket, System.Numerics.Vector3.UnitZ);
                var result = _orbs!.Step(cloud.Cluster, root, socket);
                if (result.Finished && !result.FirstArrival)
                {
                    cloud.Cluster.Dispose();
                    _clouds.Remove(cloud);
                    continue;
                }
                if (!result.FirstArrival || !cloud.Motion.Arrive()) continue;
                int granted = supply.Heal(Rac2CrateSupply.NanotechPerCloud);
                if (granted > 0)
                {
                    effects.PlayPickup(Rac2CrateSupply.NanotechClass, 1);
                    ports.RefreshHud();
                    GD.Print($"[rac2-supply] nanotech collected delta={granted}");
                }
                _orbs.Healing(cloud.Cluster);
                _clouds.Remove(cloud);
            }
            _orbs?.EndTick(root);
        }
    }

    internal void Present() { if (!_disposed && current()) _orbs?.Present(); }

    private float Sample(float min, float max) => min + _random!.NextSingle() * (max - min);
    private static System.Numerics.Vector3 ToNative(Vector3 point) => new(-point.X, point.Z, point.Y);
    private static Vector3 FromNative(System.Numerics.Vector3 point) => new(-point.X, point.Z, point.Y);
    public void Dispose()
    {
        _disposed = true;
        foreach (var node in _ammo.Select(pickup => pickup.Node))
            if (GodotObject.IsInstanceValid(node)) node.QueueFree();
        foreach (var cloud in _clouds) cloud.Cluster.Dispose();
        _orbs?.Dispose(); _orbs = null;
        _ammo.Clear(); _clouds.Clear(); _ammoModel.Clear();
        _assets = null; _materials = null;
    }
}
