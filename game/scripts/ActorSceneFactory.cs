using Godot;
using OBP.Fusion;
using OBP.Godot;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

internal interface IActorFactory
{
    ActorAdapterDescriptor Descriptor { get; }
    void RegisterUpdateGroups(ActorRuntimeSet actors);
    HostedActor Create(ActorPlacement placement, RuntimeWorld library, ActorSourceVersion source);
}

/// <summary>World-owned construction/rollback of genuine models and their contact bodies.</summary>
internal sealed class ActorSceneFactory(ActorRuntimeSet actors, Node3D parent,
    IDictionary<WeaponTargetKey, CharacterBody3D> bodies)
{
    public ActorSceneBinding Create(ActorPlacement placement, RuntimeWorld library, Func<RuntimeDynamicObject, bool> admit)
    {
        if (library.Game != placement.Source.Game || library.BuildId != placement.Source.Build || library.LevelId != placement.LevelId)
            throw new InvalidDataException("Actor library provenance does not match its placement.");
        var template = (library.DynamicObjects ?? []).OrderBy(source => source.InstanceIndex).FirstOrDefault(source =>
            source.SourceGame == placement.Source.Game && source.NativeClassId == placement.NativeClassId &&
            (!placement.SourceInstanceIndex.HasValue || source.InstanceIndex == placement.SourceInstanceIndex) && admit(source))
            ?? throw new InvalidDataException("No native instance satisfies this actor/context recipe.");
        if (template.Meshes.Count == 0) throw new InvalidDataException("The genuine actor model is missing.");
        var key = actors.Allocate(placement.Source.Game, placement.NativeClassId);
        var authored = RuntimeWorldScene.ToSceneTransform(template.Transform); authored.Origin = Vector3.Zero;
        var placed = RuntimeWorldScene.ToSceneTransform(placement.Transform) * authored;
        var cloned = template with
        {
            InstanceIndex = key.Target.InstanceIndex,
            Transform = RuntimeWorldScene.ToRuntimeTransform(placed),
            InteractionId = $"{placement.Id}/source:{template.InteractionId}/instance:{key.Target.InstanceIndex}"
        };
        var result = RuntimeWorldScene.Build(library with { DynamicObjects = [cloned] }, "Actor_" + placement.Id,
            new() { IncludeSky = false, IncludeCollision = false, IncludeMobyMarkers = false });
        try
        {
            parent.AddChild(result.Root);
            var registration = new ActorRegistration(key, placement.Source with { NativeId = template.InteractionId }, placement.AdapterId,
                placement.NativeContextId, [new("damage", CapabilityAdmission.Ready), new("native-update", CapabilityAdmission.Ready)]);
            return new(result, result.DynamicObjectNodes!.Single(), registration, bodies);
        }
        catch { result.Root.GetParent()?.RemoveChild(result.Root); result.Root.QueueFree(); throw; }
    }
}

internal sealed class ActorSceneBinding(RuntimeWorldScene.Result scene, RuntimeWorldScene.DynamicObjectNode node,
    ActorRegistration registration, IDictionary<WeaponTargetKey, CharacterBody3D> bodies) : IDisposable
{
    private CharacterBody3D? _body;
    private bool _disposed;
    public RuntimeWorldScene.DynamicObjectNode Node { get; } = node;
    public ActorRegistration Registration { get; } = registration;
    private WeaponTargetKey Target => Registration.Instance.Target;

    public CharacterBody3D AddBody()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_body is not null) throw new InvalidOperationException("Actor already has a contact body.");
        Aabb? bounds = null;
        foreach (var mesh in Node.Root.GetChildren().OfType<MeshInstance3D>())
        { var next = mesh.GlobalTransform * mesh.GetAabb(); bounds = bounds?.Merge(next) ?? next; }
        var box = bounds ?? throw new InvalidDataException("Actor has no genuine model contact bounds.");
        _body = new() { Name = "ActorContact_" + Node.Source.InstanceIndex, CollisionLayer = 1, CollisionMask = 1 };
        _body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = box.Size.Max(Vector3.One * .1f) }, Position = box.GetCenter() - Node.Root.GlobalPosition });
        scene.Root.AddChild(_body); _body.GlobalPosition = Node.Root.GlobalPosition;
        bodies.Add(Target, _body);
        return _body;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_body is { } body)
        {
            if (GodotObject.IsInstanceValid(body)) body.CollisionLayer = body.CollisionMask = 0;
            if (bodies.TryGetValue(Target, out var registered) && ReferenceEquals(registered, body)) bodies.Remove(Target);
        }
        if (!GodotObject.IsInstanceValid(scene.Root)) return;
        scene.Root.GetParent()?.RemoveChild(scene.Root);
        scene.Root.QueueFree();
    }
}
