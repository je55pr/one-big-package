using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

public partial class OBPGame
{
    private readonly List<(Rac1NovalisMechanism Mechanism, RuntimeWorldScene.DynamicObjectNode Node)> _rac1NovalisMechanisms = [];

    private void ConfigureRac1NovalisMechanisms(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        if (world.LevelId != 1 || world.BuildId != "rac1-ntscu-original") return;
        var source = Sources.Setup.Library.Get(ObpSourceGame.Rac1) ?? throw new InvalidOperationException("Novalis mechanisms require the retail source.");
        using var reader = new FileRandomAccessReader(source.Path);
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
            AddRac1MechanismCollider(node);
            _rac1NovalisMechanisms.Add((mechanism, node));
        }
        GD.Print($"[rac1-novalis-route] {_rac1NovalisMechanisms.Count} source-driven doors, pistons and staircase rocks");
    }

    private static void AddRac1MechanismCollider(RuntimeWorldScene.DynamicObjectNode node)
    {
        // Explicit host geometry: original render triangles, not a recovered
        // PS2 Moby collision primitive. Preserve the authored scale/rotation.
        var faces = node.Root.GetChildren().OfType<MeshInstance3D>()
            .Where(mesh => mesh.Mesh is not null)
            .SelectMany(mesh => mesh.Mesh.GetFaces().Select(vertex => mesh.Transform * vertex)).ToArray();
        if (faces.Length == 0) throw new InvalidDataException("Mechanism has no solid render geometry.");
        var body = new StaticBody3D { Name = "NovalisMechanismHostContact", CollisionLayer = 1, CollisionMask = 1 };
        body.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = faces, BackfaceCollision = true } });
        node.Root.AddChild(body);
    }

    private void StepRac1NovalisMechanisms()
    {
        if (_rac1NovalisMechanisms.Count == 0 || MovementReproduction.IsLoading || _player is null ||
            _captureReplay?.Recorder is { WaitingForEntry: true }) return;
        NVector3 player = new(-_player.GlobalPosition.X, _player.GlobalPosition.Z, _player.GlobalPosition.Y);
        Vector3 sceneCamera = _player.Camera.GlobalPosition;
        NVector3 camera = new(-sceneCamera.X, sceneCamera.Z, sceneCamera.Y);
        foreach (var (mechanism, node) in _rac1NovalisMechanisms)
        {
            if (mechanism is Rac1NovalisFallingRock rock) rock.PlayerRunMode = _player.Rac1GameplayAlive ? 0 : -1;
            mechanism.Step(player, camera);
            NVector3 position = mechanism.Position;
            Vector3 target = new(-position.X, position.Z, position.Y);
            if (node.Root.GlobalPosition != target) node.Root.GlobalPosition = target;
            if (mechanism.Sound is int sound) Rac1World.Loot.PlayRac1PickupSound(mechanism.NativeClassId, sound, node.Root.GlobalPosition);
        }
    }
}
