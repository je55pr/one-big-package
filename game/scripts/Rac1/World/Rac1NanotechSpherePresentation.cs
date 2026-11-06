using Godot;
using OBP.Godot;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

/// <summary>
/// Explicit RAC1 presentation donor for another world's health supplies. Uses
/// the existing retail cluster, particle, geometry and material implementations;
/// the recipient world retains admission, health credit, sounds and lifetime.
/// </summary>
internal sealed class Rac1NanotechSpherePresentation : IDisposable
{
    private readonly RuntimeWorldScene.Result _scene;
    private readonly Rac1NanotechOrbAssets _assets;
    private readonly Rac1NanotechOrbSpin _spin = new();
    private readonly Rac1PickupRandom _random;
    private readonly Rac1ParticleSession _particles;
    private readonly Rac1FxMaterials _materials;
    private readonly ImmediateMesh _mesh = new();
    private readonly MeshInstance3D _meshNode;
    private readonly Rac1ParticleView _particleView;
    private readonly Dictionary<int, (RuntimeWorldScene.DynamicObjectNode Crate, Rac1NanotechCluster Cluster)> _riders = [];
    private readonly List<Rac1NanotechCluster> _healing = [];
    private readonly List<Rac1NanotechCluster> _drawn = [];
    private NVector3 _eye;
    private Vector3 _eyeScene;
    private Camera3D? _camera;
    private bool _disposed;

    internal Rac1NanotechSpherePresentation(RuntimeWorldScene.Result scene, string sourcePath, int seed,
        IEnumerable<RuntimeWorldScene.DynamicObjectNode> crates)
    {
        _scene = scene;
        using var reader = new FileRandomAccessReader(sourcePath);
        // Deliberate donor identity: RAC1 Veldin, never the recipient level ID.
        var donor = Rac1DiscIndex.Read(reader).Levels.Single(level => level.LevelId == 0);
        _assets = Rac1NanotechOrbAssets.Decode(Rac1ExecutableOverlay.Read(reader, donor), Rac1LevelCore.Open(reader, donor));
        _random = Rac1PickupRandom.FromSeed(unchecked((uint)seed)).Fork();
        _particles = new(_assets.Particles, _random);
        _materials = new(_assets.FxTextures, priorityBias: -10);
        _meshNode = new MeshInstance3D
        {
            Name = "Rac1DonorNanotechSphereMesh",
            Mesh = _mesh,
            TopLevel = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _particleView = new Rac1ParticleView
        {
            Name = "Rac1DonorNanotechParticles",
            SourceBank = new(OBP.Core.ObpSourceGame.Rac1, OBP.RAC1.Rac1Authority.Primary.BuildId, "LEVEL0"),
        };
        try
        {
            scene.Root.AddChild(_meshNode);
            scene.Root.AddChild(_particleView);
            foreach (var crate in crates)
                _riders.Add(crate.Source.InstanceIndex, (crate, Create(ToNative(crate.Root.GlobalPosition))));
        }
        catch
        {
            Dispose();
            throw;
        }
        GD.Print($"[rac2-supply] RAC1 Veldin nanotech sphere donor ready: riders={_riders.Count}");
    }

    private Rac1NanotechCluster Create(NVector3 origin) => new(origin, _assets, _spin, _particles, _random);

    internal Rac1NanotechCluster Break(RuntimeWorldScene.DynamicObjectNode crate)
    {
        var cluster = _riders.Remove(crate.Source.InstanceIndex, out var rider) ? rider.Cluster : Create(ToNative(crate.Root.GlobalPosition));
        cluster.Follow(ToNative(crate.Root.GlobalPosition));
        cluster.CrateBroken();
        return cluster;
    }

    internal void BeginTick(NVector3 player, NVector3 socket)
    {
        _camera = _scene.Root.GetViewport()?.GetCamera3D();
        _eyeScene = _camera?.GlobalPosition ?? FromNative(player) + Vector3.Up * 5;
        _eye = ToNative(_eyeScene);
        _drawn.Clear();
        _spin.Step(_assets.K.SpinStep, _assets.K.OrbRadius);
        foreach (var (id, rider) in _riders.ToArray())
        {
            if (!GodotObject.IsInstanceValid(rider.Crate.Root) || !rider.Crate.Root.Visible)
            {
                rider.Cluster.Dispose();
                _riders.Remove(id);
                continue;
            }
            rider.Cluster.Follow(ToNative(rider.Crate.Root.GlobalPosition));
            Step(rider.Cluster, player, socket);
        }
        foreach (var cluster in _healing.ToArray())
            if (Step(cluster, player, socket).Finished)
            {
                cluster.Dispose();
                _healing.Remove(cluster);
            }
    }

    internal Rac1NanotechCluster.Result Step(Rac1NanotechCluster cluster, NVector3 player, NVector3 socket)
    {
        var position = FromNative(cluster.Position);
        bool visible = _camera is null || _eyeScene.DistanceTo(position) < 48 && _camera.IsPositionInFrustum(position);
        var result = cluster.Step(new(player, socket, NVector3.UnitZ, visible));
        if (visible) _drawn.Add(cluster);
        return result;
    }

    internal void Healing(Rac1NanotechCluster cluster) => _healing.Add(cluster);

    internal void EndTick(NVector3 player)
    {
        _particles.Hero = player;
        _particles.Step(_eye, enforceLevelBounds: false);
        _particleView.Render(_particles, _assets.Particles.PartTextures);
    }

    internal void Present()
    {
        if (_disposed) return;
        var camera = _scene.Root.GetViewport()?.GetCamera3D();
        var eye = camera is null ? _eye : ToNative(camera.GlobalPosition);
        _materials.Write(_mesh, _drawn.SelectMany(cluster => cluster.Build(eye)), FromNative);
    }

    private static NVector3 ToNative(Vector3 point) => new(-point.X, point.Z, point.Y);
    private static Vector3 FromNative(NVector3 point) => new(-point.X, point.Z, point.Y);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var cluster in _riders.Values.Select(rider => rider.Cluster).Concat(_healing)) cluster.Dispose();
        _riders.Clear(); _healing.Clear(); _drawn.Clear();
        _particles.Clear();
        _mesh.ClearSurfaces();
        foreach (var node in new Node[] { _meshNode, _particleView })
            if (GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion()) node.QueueFree();
        _materials.Dispose();
    }
}
