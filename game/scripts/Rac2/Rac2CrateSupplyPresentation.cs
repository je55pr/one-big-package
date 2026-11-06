using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC2.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

internal sealed record Rac2CrateSupplyPorts(
    Func<(int Current, int Capacity)> Health,
    Func<int, int> Heal,
    Func<int, IReadOnlyList<PlayerAmmoSupply>> Ammo,
    Func<SourceWeaponKey, int, int> GrantAmmo);

/// <summary>Lease-owned supply pickups, using original ammo geometry and shared crate effect ownership.</summary>
internal sealed class Rac2CrateSupplyPresentation(RuntimeWorld world, RuntimeWorldScene.Result scene,
    Rac2CrateBoltPorts ports, Rac2CrateSupplyPorts supply, Rac2CrateEffects effects, Func<bool> current) : IDisposable
{
    private sealed record Ammo(Node3D Node, SourceWeaponKey Weapon, int Amount, Rac2AmmoPickup Motion);
    private sealed class Cloud(Node3D node, Rac2NanotechCloud motion)
    {
        internal Node3D Node { get; } = node;
        internal Rac2NanotechCloud Motion { get; } = motion;
        internal int Travel;
        internal int Age;
    }
    private readonly List<Ammo> _ammo = [];
    private readonly List<Cloud> _clouds = [];
    private readonly List<(ArrayMesh Mesh, Material Material)> _ammoModel = [];
    private Rac2SupplyAssets.Catalogue? _assets;
    private WorldMaterialFactory? _materials;
    private Random? _random;
    private double _accumulator;
    private bool _disposed;

    internal void Prepare()
    {
        var path = ports.SourcePath() ?? throw new InvalidDataException("R&C2 supply source is unavailable.");
        _assets = Rac2SupplyAssets.Load(path, world.LevelId);
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
    }

    internal void Spawn(RuntimeWorldScene.DynamicObjectNode crate)
    {
        if (_disposed || !current() || _assets is null) return;
        var origin = crate.Root.GlobalPosition + Vector3.Up * .5f;
        if (crate.Source.NativeClassId == Rac2CrateSupply.NanotechClass)
        {
            var node = new Node3D { Name = $"Rac2NanotechCloud_{crate.Source.InstanceIndex}" };
            // Eight native cloud particles represent one health credit. Sprite
            // splines/GS blending are not recovered; these wisps are host display.
            var material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new(.25f, .65f, 1),
                EmissionEnabled = true,
                Emission = new(.25f, .55f, 1),
            };
            for (int i = 0; i < Rac2CrateSupply.ParticleCount; i++)
                node.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = .035f, Height = .07f }, MaterialOverride = material });
            scene.Root.AddChild(node);
            node.GlobalPosition = origin;
            _clouds.Add(new(node, new(ToNative(origin))));
            GD.Print($"[rac2-supply] crate={crate.Source.InstanceIndex} nanotech cloud=1 particles=8");
            return;
        }
        var owned = supply.Ammo(_assets.Lancer.Amount);
        PlayerAmmoSupply? selected;
        if (owned.All(item => item.Weapon.SourceGame == ObpSourceGame.Rac2))
        {
            int index = Rac2CrateSupply.SelectAmmo(owned.Select(item => (item.Weapon.NativeItemId, item.Count, item.Capacity)).ToArray(), _random!.Next);
            selected = owned.FirstOrDefault(item => item.Weapon.NativeItemId == index);
        }
        else selected = PlayerAmmoSupplyPolicy.Select(owned, _random!.Next);
        if (selected is null)
        {
            GD.Print("[rac2-supply] no owned implemented ammo recipient; no item-number substitution");
            return;
        }
        int count = Rac2CrateSupply.AmmoPieceCount(_random!.Next(5));
        for (int i = 0; i < count; i++)
        {
            var node = new Node3D { Name = $"Rac2Ammo_{crate.Source.InstanceIndex}_{i}_{selected.Weapon.SourceGame}_{selected.Weapon.NativeItemId}" };
            foreach (var model in _ammoModel) node.AddChild(new MeshInstance3D { Mesh = model.Mesh, MaterialOverride = model.Material });
            scene.Root.AddChild(node);
            node.GlobalPosition = origin;
            _ammo.Add(new(node, selected.Weapon, selected.Amount, new(ToNative(origin), Sample)));
        }
        GD.Print($"[rac2-supply] crate={crate.Source.InstanceIndex} ammo recipient={selected.Weapon} pieces={count} amount={selected.Amount}");
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
            foreach (var pickup in _ammo.ToArray())
            {
                var profile = ports.AttractionProfile();
                var available = supply.Ammo(_assets!.Lancer.Amount).FirstOrDefault(entry => entry.Weapon == pickup.Weapon);
                if (ports.Alive() && available is not null && available.Count < available.Capacity && pickup.Motion.CanCollect(root, profile.Radius, profile.Height))
                {
                    int granted = supply.GrantAmmo(pickup.Weapon, pickup.Amount);
                    if (granted > 0)
                    {
                        pickup.Motion.Claim();
                        effects.PlayPickup(Rac2CrateSupply.AmmoPickupClass);
                        ports.RefreshHud();
                        GD.Print($"[rac2-supply] ammo collected recipient={pickup.Weapon} delta={granted}");
                    }
                }
                pickup.Motion.Step(socket, (from, to) =>
                {
                    var query = PhysicsRayQueryParameters3D.Create(FromNative(from) + Vector3.Up * .1f, FromNative(to));
                    query.Exclude = excluded;
                    var hit = space.IntersectRay(query);
                    return hit.TryGetValue("position", out var point) ? ToNative(point.AsVector3()) : null;
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
                cloud.Motion.TryAttract(root, ports.Alive() ? health.Current : 0, health.Capacity, pending, visible);
                cloud.Age++;
                // Host travel duration; native particle interpolation remains a separate recovery task.
                if (cloud.Motion.Attracting && ports.Alive()) cloud.Travel = Math.Min(45, cloud.Travel + 1);
                float travel = cloud.Travel / 45f;
                cloud.Node.GlobalPosition = FromNative(System.Numerics.Vector3.Lerp(cloud.Motion.Origin, socket, travel));
                int i = 0;
                foreach (var particle in cloud.Node.GetChildren().OfType<MeshInstance3D>())
                {
                    float phase = cloud.Age / 15f + i++ * MathF.Tau / Rac2CrateSupply.ParticleCount;
                    particle.Position = new Vector3(MathF.Cos(phase), MathF.Sin(phase * .7f), MathF.Sin(phase)) * .22f * (1 - travel);
                }
                if (cloud.Travel < 45 || !cloud.Motion.Arrive()) continue;
                int granted = supply.Heal(Rac2CrateSupply.NanotechPerCloud);
                if (granted > 0)
                {
                    effects.PlayPickup(Rac2CrateSupply.NanotechClass, 1);
                    ports.RefreshHud();
                    GD.Print($"[rac2-supply] nanotech collected delta={granted}");
                }
                cloud.Node.QueueFree();
                _clouds.Remove(cloud);
            }
        }
    }

    private float Sample(float min, float max) => min + _random!.NextSingle() * (max - min);
    private static System.Numerics.Vector3 ToNative(Vector3 point) => new(-point.X, point.Z, point.Y);
    private static Vector3 FromNative(System.Numerics.Vector3 point) => new(-point.X, point.Z, point.Y);
    public void Dispose()
    {
        _disposed = true;
        foreach (var node in _ammo.Select(pickup => pickup.Node).Concat(_clouds.Select(cloud => cloud.Node)))
            if (GodotObject.IsInstanceValid(node)) node.QueueFree();
        _ammo.Clear(); _clouds.Clear(); _ammoModel.Clear();
        _assets = null; _materials = null;
    }
}
