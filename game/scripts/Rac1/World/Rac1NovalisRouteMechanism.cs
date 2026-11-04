using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

/// <summary>Source-authored Novalis proximity doors, pistons and falling rock staircase.</summary>
internal sealed class Rac1NovalisRouteMechanism(Rac1AuthoredMechanismPorts ports) : Rac1AuthoredMechanism
{
    private readonly List<(Rac1NovalisMechanism Mechanism, RuntimeWorldScene.DynamicObjectNode Node)> _mechanisms = [];
    private readonly List<StaticBody3D> _colliders = [];
    private readonly Dictionary<int, Vector3> _originalPositions = [];
    internal IReadOnlyList<(Rac1NovalisMechanism Mechanism, RuntimeWorldScene.DynamicObjectNode Node)> Mechanisms => _mechanisms;

    internal override void Configure(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        if (world.LevelId != 1 || world.BuildId != OBP.RAC1.Rac1Authority.Primary.BuildId) return;
        var path = ports.SourcePath() ?? throw new InvalidOperationException("Novalis mechanisms require the retail source.");
        using var reader = new FileRandomAccessReader(path);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == 1);
        byte[] gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
        var volumes = Rac1ClipVolumes.Read(gameplay);
        foreach (var authored in Rac1Instances.Parse(gameplay).MobyInstances)
        {
            Rac1NovalisMechanism? mechanism = authored.OClass switch
            {
                Rac1NovalisProximityDoor.LeftClass or Rac1NovalisProximityDoor.RightClass => new Rac1NovalisProximityDoor(authored),
                Rac1NovalisPiston.SmallClass or Rac1NovalisPiston.LargeClass => new Rac1NovalisPiston(authored),
                Rac1NovalisFallingRock.ClassId => new Rac1NovalisFallingRock(authored, volumes),
                _ => null,
            };
            if (mechanism is null) continue;
            var node = result.DynamicObjectNodes?.Single(n => n.Source.InstanceIndex == authored.Index)
                ?? throw new InvalidDataException("Novalis mechanism lacks its imported model.");
            _originalPositions.Add(authored.Index, node.Root.GlobalPosition);
            AddCollider(node);
            _mechanisms.Add((mechanism, node));
        }
        GD.Print($"[rac1-novalis-route] {_mechanisms.Count} source-driven doors, pistons and staircase rocks");
    }

    private void AddCollider(RuntimeWorldScene.DynamicObjectNode node)
    {
        // Explicit host geometry: source render triangles, not a recovered
        // PS2 Moby collision primitive. Preserve authored scale/rotation.
        var faces = node.Root.GetChildren().OfType<MeshInstance3D>()
            .Where(mesh => mesh.Mesh is not null)
            .SelectMany(mesh => mesh.Mesh.GetFaces().Select(vertex => mesh.Transform * vertex)).ToArray();
        if (faces.Length == 0) throw new InvalidDataException("Mechanism has no solid render geometry.");
        var body = new StaticBody3D { Name = "NovalisMechanismHostContact", CollisionLayer = 1, CollisionMask = 1 };
        body.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = faces, BackfaceCollision = true } });
        node.Root.AddChild(body);
        _colliders.Add(body);
    }

    internal override void PhysicsStep(PlayerHost? player, bool suspended, bool waitingForReplay)
    {
        if (_mechanisms.Count == 0 || suspended || player is null || !GodotObject.IsInstanceValid(player) ||
            waitingForReplay) return;
        NVector3 playerPosition = new(-player.GlobalPosition.X, player.GlobalPosition.Z, player.GlobalPosition.Y);
        Vector3 sceneCamera = player.Camera.GlobalPosition;
        NVector3 camera = new(-sceneCamera.X, sceneCamera.Z, sceneCamera.Y);
        foreach (var (mechanism, node) in _mechanisms)
        {
            if (mechanism is Rac1NovalisFallingRock rock) rock.PlayerRunMode = player.Rac1GameplayAlive ? 0 : -1;
            mechanism.Step(playerPosition, camera);
            NVector3 position = mechanism.Position;
            Vector3 target = new(-position.X, position.Z, position.Y);
            if (node.Root.GlobalPosition != target) node.Root.GlobalPosition = target;
            if (mechanism.Sound is int sound)
                ports.PlaySourceSound(mechanism.NativeClassId, sound, node.Root.GlobalPosition);
        }
    }

    public override void Dispose()
    {
        foreach (var collider in _colliders)
        {
            if (!GodotObject.IsInstanceValid(collider)) continue;
            collider.CollisionLayer = 0;
            if (collider.GetParent() is { } parent) parent.RemoveChild(collider);
            collider.Free();
        }
        foreach (var (mechanism, node) in _mechanisms)
            if (GodotObject.IsInstanceValid(node.Root) && _originalPositions.TryGetValue(mechanism.InstanceIndex, out var position))
                node.Root.GlobalPosition = position;
        _mechanisms.Clear();
        _colliders.Clear();
        _originalPositions.Clear();
    }
}
