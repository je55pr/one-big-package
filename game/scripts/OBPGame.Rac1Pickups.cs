using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime;
using OBP.Runtime.Audio;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

public partial class OBPGame
{
    private Rac1PickupAssets.Catalogue? _rac1PickupAssets;
    private WorldMaterialFactory? _rac1PickupMaterials;
    private readonly Dictionary<int, Rac1BoltMotionSession> _rac1PickupMotion = [];
    private readonly List<(Node3D Node, Rac1CrateDebris Motion)> _rac1Debris = [];
    private readonly Dictionary<int, StaticBody3D> _rac1CrateBodies = [];
    private readonly Dictionary<int, (Node3D Node, Rac1CrateFallSession Motion, float FootOffset)> _rac1FallingCrates = [];
    private readonly Dictionary<int, List<(ArrayMesh Mesh, Material Material)>> _rac1EffectMeshes = [];
    private Rac1PickupRandom? _rac1EffectRandom;
    private Rac1PickupRandom Rac1EffectRandom => _rac1EffectRandom ??=
        Rac1PickupRandom.FromSeed(unchecked((uint)(_args.FixedSeed ?? System.Environment.TickCount)));
    private double _rac1PickupTickAccumulator;
    private int _rac1BoltSoundDelay;
    private int _rac1PickupSoundCount;

    private void ResetRac1PickupPresentation()
    {
        _playerAudio?.RetireWorld();
        ResetRac1PlacedBoltPresentation();
        ResetRac1CrateSupply();
        foreach (var node in Rac1World.Presentation.PickupNodes.Values.Concat(_rac1Debris.Select(piece => piece.Node)))
            if (IsInstanceValid(node)) node.QueueFree();
        _rac1PickupMotion.Clear();
        _rac1Debris.Clear();
        _rac1EffectMeshes.Clear();
        _equipmentPresentation.RetireWorld();
        _rac1CrateBodies.Clear();
        _rac1FallingCrates.Clear();
        _rac1PickupTickAccumulator = 0;
        _rac1BoltSoundDelay = 0;
        _rac1PickupSoundCount = 0;
        _rac1PickupAssets = null;
        _rac1PickupMaterials = null;
    }

    private void ConfigureRac1PickupPresentation(RuntimeWorld world)
    {
        var source = Sources.Setup.Library.Get(OBP.Core.ObpSourceGame.Rac1);
        if (source is null) throw new InvalidOperationException("R&C1 pickup presentation requires the active source.");
        _rac1PickupAssets = world.LevelId == 0 && _equipmentPresentation.SelectedAssets is not null
            ? _equipmentPresentation.SelectedAssets : Rac1PickupAssets.Load(source.Path, world.LevelId);
        _rac1PickupMaterials = new WorldMaterialFactory(world with { Textures = _rac1PickupAssets.Textures });
        GD.Print($"[rac1-pickups] loaded {_rac1PickupAssets.Models.Count} retail bolt/debris models");
    }

    private Node3D CreateRac1EffectNode(int nativeClass, string name, bool playerEquipment = false)
    {
        var assets = playerEquipment ? _equipmentPresentation.Assets(OrdinaryPlayerSession, _rac1PickupAssets) : _rac1PickupAssets;
        var materials = playerEquipment ? _equipmentPresentation.EffectMaterials : _rac1PickupMaterials;
        var cache = playerEquipment ? _equipmentPresentation.EffectMeshes : _rac1EffectMeshes;
        if (_sceneResult is null || materials is null || assets is null ||
            !assets.Models.TryGetValue(nativeClass, out var model))
            throw new InvalidDataException($"Retail R&C1 effect class {nativeClass} is unavailable.");
        if (!cache.TryGetValue(nativeClass, out var surfaces))
        {
            surfaces = [];
            foreach (var surface in model.Surfaces)
            {
                var vertices = new Vector3[surface.Positions.Length / 3];
                for (int i = 0; i < vertices.Length; i++)
                    vertices[i] = RuntimeWorldScene.ToScene(surface.Positions[i * 3], surface.Positions[i * 3 + 1], surface.Positions[i * 3 + 2]);
                var uvs = new Vector2[surface.Uvs.Length / 2];
                for (int i = 0; i < uvs.Length; i++) uvs[i] = new(surface.Uvs[i * 2], surface.Uvs[i * 2 + 1]);
                int[] indices = (int[])surface.Indices.Clone();
                for (int i = 0; i < indices.Length; i += 3) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
                var arrays = new global::Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = vertices;
                arrays[(int)Mesh.ArrayType.TexUV] = uvs;
                arrays[(int)Mesh.ArrayType.Index] = indices;
                var mesh = new ArrayMesh();
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                var material = materials.Instanced("moby", surface.TextureId, false, surface.MaterialPresentation);
                surfaces.Add((mesh, material));
            }
            cache.Add(nativeClass, surfaces);
        }
        var node = new Node3D { Name = name };
        foreach (var surface in surfaces)
            node.AddChild(new MeshInstance3D { Mesh = surface.Mesh, MaterialOverride = surface.Material });
        _sceneResult.Root.AddChild(node);
        return node;
    }

    private void SpawnRac1BoltPickups(Vector3 origin, IReadOnlyList<Rac1BoltPickup> pickups)
    {
        foreach (var pickup in pickups)
        {
            var motion = Rac1BoltMotionSession.FromCrate(pickup.NativeClassId, ToEffectNative(origin), Rac1EffectRandom);
            var node = CreateRac1EffectNode(pickup.NativeClassId, $"rac1_retail_bolt_{pickup.PickupId}_class{pickup.NativeClassId}");
            node.GlobalPosition = FromEffectNative(motion.Position);
            node.Scale = Vector3.One * motion.VisualScale;
            Rac1World.Presentation.PickupNodes.Add(pickup.PickupId, node);
            _rac1PickupMotion.Add(pickup.PickupId, motion);
        }
    }

    private void SpawnRac1CrateDebris(RuntimeWorldScene.DynamicObjectNode crate)
    {
        NVector3 origin = ToEffectNative(crate.Root.GlobalPosition);
        NVector3 Axis(NVector3 local) => ToEffectNative(crate.Root.ToGlobal(FromEffectNative(local))) - origin;
        NVector3 x = Axis(NVector3.UnitX), y = Axis(NVector3.UnitY), z = Axis(NVector3.UnitZ);
        var transform = new System.Numerics.Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0,
            z.X, z.Y, z.Z, 0, origin.X, origin.Y, origin.Z, 1);
        foreach (var motion in Rac1CrateDebris.Break(crate.Source.NativeClassId, transform, Rac1EffectRandom))
        {
            int id = motion.NativeClassId;
            if (_rac1PickupAssets?.Models.ContainsKey(id) != true) continue;
            var node = CreateRac1EffectNode(id, $"rac1_crate_debris_class{id}");
            node.GlobalPosition = FromEffectNative(motion.Position);
            node.Quaternion = FromEffectOrientation(motion.Orientation);
            node.Scale = Vector3.One * motion.Scale;
            _rac1Debris.Add((node, motion));
        }
    }

    private void TickRac1Pickups(double delta)
    {
        if (_player is null || _sceneResult is null) return;
        InitializeRac1PlacedBolts();
        _rac1PickupTickAccumulator += Math.Max(0, delta) * 60;
        int collectedValue = 0;
        while (_rac1PickupTickAccumulator >= 1)
        {
            _rac1PickupTickAccumulator--;
            if (_rac1BoltSoundDelay > 0) _rac1BoltSoundDelay--;
            TickRac1CrateSupport();
            var root = ToEffectNative(_player.GlobalPosition);
            // The host feet-to-native target socket mapping is an adapter.
            var socket = root + new NVector3(0, 0, .7f);
            foreach (var pair in _rac1PickupMotion.ToArray())
            {
                var motion = pair.Value;
                var node = Rac1World.Presentation.PickupNodes[pair.Key];
                if (!IsInstanceValid(node))
                {
                    if (!Rac1World.Context.BoltCrates.DiscardPickup(pair.Key)) Rac1World.Context.BoltPickups.Discard(pair.Key);
                    _rac1PickupMotion.Remove(pair.Key);
                    Rac1World.Presentation.PickupNodes.Remove(pair.Key);
                    continue;
                }
                bool wrenchContact = _rac1WrenchThrow.Active &&
                    node.GlobalPosition.DistanceTo(WrenchWorldPosition()) < Rac1WrenchThrowSession.BoltCollectionRadius;
                var contact = motion.NeedsWorldContact ? ProbeRac1EffectContact(motion.Position,
                    motion.Position + motion.Velocity - new NVector3(0, 0, Rac1BoltMotionSession.Gravity / 3600), motion.CollisionRadius) : null;
                int previousState = motion.NativeState;
                var attraction = Rac1BoltMotionSession.AttractionProfile(Rac1World.Context.NativePlayerState20A4, _rac1Weapons.OwnsNativeItem(34));
                motion.Step(root, socket, contact, attractEnabled: !_rac1Nanotech.Probe().IsDead,
                    thrownWrenchContact: wrenchContact,
                    attractPlanarRadius: attraction.PlanarRadius, attractHeight: attraction.Height);
                if (previousState != 4 && motion.NativeState == 4 && _rac1BoltSoundDelay == 0)
                {
                    PlayRac1PickupSound(motion.NativeClassId, 0, node.GlobalPosition);
                    _rac1BoltSoundDelay = 1 + Rac1EffectRandom.Below(3);
                }
                node.GlobalPosition = FromEffectNative(motion.Position);
                node.Quaternion = FromEffectOrientation(motion.Orientation);
                if (!motion.Collected) continue;
                int value = CollectRac1HostedBolt(pair.Key, node);
                _rac1BoltWallet.Credit(value);
                collectedValue += value;
                _rac1PickupMotion.Remove(pair.Key);
                Rac1World.Presentation.PickupNodes.Remove(pair.Key);
            }
            TickRac1CrateSupply(root, socket);
            for (int i = _rac1Debris.Count - 1; i >= 0; i--)
            {
                var piece = _rac1Debris[i];
                var motion = piece.Motion;
                var contact = ProbeRac1EffectContact(motion.Position,
                    motion.Position + motion.Velocity - new NVector3(0, 0, 20f / 3600), .1f);
                motion.Step(contact);
                piece.Node.GlobalPosition = FromEffectNative(motion.Position);
                piece.Node.Quaternion = FromEffectOrientation(motion.Orientation);
                piece.Node.Scale = Vector3.One * motion.Scale;
                foreach (var mesh in piece.Node.GetChildren().OfType<MeshInstance3D>()) mesh.Transparency = 1 - motion.Alpha / 128f;
                if (!motion.Expired) continue;
                piece.Node.QueueFree();
                _rac1Debris.RemoveAt(i);
            }
        }
        if (collectedValue > 0)
        {
            PersistRac1CampaignState("bolt collection");
            Rac1World.Context.CombatStatus = $"collected +{collectedValue} bolts";
            RefreshRac1HudState(Rac1HudProjection.CollectedBoltFeedback(collectedValue));
        }
    }

    private void PlayRac1PickupSound(int nativeClass, int localSound, Vector3 position)
    {
        if (_rac1PickupAssets?.Sounds is not { } sounds || !sounds.TryGetValue((nativeClass, localSound), out var sound)) return;
        var clip = sound.SelectPitch(Rac1EffectRandom);
        if (clip.FrameCount == 0) return; // A native probability program deliberately selected silence.
        if (!_worldHost.PlayEffect(new RuntimeAudioPlaybackIntent(clip, RuntimeAudioCategory.SoundEffect,
            gain: sound.Gain, position: new RuntimeAudioPosition(-position.X, position.Y, position.Z)))) return;
        _rac1PickupSoundCount++;
        RecordRac1WeaponSound(nativeClass, localSound);
    }

    private Rac1PickupContact? ProbeRac1EffectContact(NVector3 from, NVector3 to, float radius)
    {
        if (_player is null) return null;
        // Godot ray contact is a host approximation of the native swept sphere.
        Vector3 start = FromEffectNative(from), end = FromEffectNative(to);
        var query = PhysicsRayQueryParameters3D.Create(start + Vector3.Up * radius, end - Vector3.Up * radius);
        query.Exclude = new global::Godot.Collections.Array<Rid> { _player.GetRid() };
        var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (!hit.TryGetValue("position", out var position) || !hit.TryGetValue("normal", out var normal)) return null;
        Vector3 n = normal.AsVector3();
        return new(ToEffectNative(position.AsVector3() + n * radius), ToEffectNative(n));
    }

    private void AddRac1CrateCollider(RuntimeWorldScene.DynamicObjectNode node)
    {
        var meshes = node.Root.GetChildren().OfType<MeshInstance3D>().ToArray();
        if (meshes.Length == 0) return;
        Aabb bounds = meshes[0].GetAabb();
        foreach (var mesh in meshes.Skip(1)) bounds = bounds.Merge(mesh.GetAabb());
        var body = new StaticBody3D { Name = "CrateContactBounds" };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = bounds.Size }, Position = bounds.GetCenter() });
        node.Root.AddChild(body);
        _rac1CrateBodies.Add(node.Source.InstanceIndex, body);
        _rac1FallingCrates.Add(node.Source.InstanceIndex,
            (node.Root, new(node.Root.GlobalPosition.Y + bounds.Position.Y), bounds.Position.Y));
    }

    private void DisableRac1CrateCollider(int instance)
    {
        _rac1FallingCrates.Remove(instance);
        if (!_rac1CrateBodies.Remove(instance, out var body) || !IsInstanceValid(body)) return;
        body.CollisionLayer = body.CollisionMask = 0;
        body.QueueFree();
    }

    private void TickRac1CrateSupport()
    {
        if (_player is null) return;
        foreach (var entry in _rac1FallingCrates.OrderBy(entry => entry.Value.Motion.Height))
        {
            var crate = entry.Value;
            if (!IsInstanceValid(crate.Node)) continue;
            Vector3 foot = crate.Node.GlobalPosition + Vector3.Up * crate.FootOffset;
            var query = PhysicsRayQueryParameters3D.Create(foot + Vector3.Up * .04f,
                foot - Vector3.Up * (Rac1CrateFallSession.MaximumFallPerTick + .02f));
            query.Exclude = new global::Godot.Collections.Array<Rid> { _player.GetRid(), _rac1CrateBodies[entry.Key].GetRid() };
            var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(query);
            float? support = hit.TryGetValue("position", out var position) && hit.TryGetValue("normal", out var normal) &&
                normal.AsVector3().Y > .70710677f ? position.AsVector3().Y : null;
            crate.Motion.Step(support);
            var current = crate.Node.GlobalPosition;
            crate.Node.GlobalPosition = new(current.X, crate.Motion.Height - crate.FootOffset, current.Z);
        }
    }

    private static NVector3 ToEffectNative(Vector3 point) => new(-point.X, point.Z, point.Y);
    private static Vector3 FromEffectNative(NVector3 point) => new(-point.X, point.Z, point.Y);
    private static Quaternion FromEffectOrientation(System.Numerics.Quaternion rotation) => new(-rotation.X, rotation.Z, rotation.Y, rotation.W);
}
