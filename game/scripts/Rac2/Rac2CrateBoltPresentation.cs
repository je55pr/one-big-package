using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.Fusion;
using OBP.RAC2.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Audio;

namespace OBP.Game;

/// <summary>Explicit lease and native-grant ports for R&C2 crate bolt presentation.</summary>
internal sealed record Rac2CrateBoltPorts(
    Func<PlayerHost?> Player,
    Func<bool> Alive,
    Func<bool> Current,
    Func<bool> HasNativePlayer,
    Func<int> WalletBalance,
    Func<int, int, ActorInstanceKey> ActorKey,
    Func<ActorInstanceKey, bool> ActorCurrent,
    Action<ActorInstanceKey, string, string, Func<bool>> RegisterActor,
    Func<ActorInstanceKey, int, int, string?> Credit,
    Action RefreshHud,
    Action<int> PickupAudio,
    Func<string?> SourcePath,
    Func<int> Seed,
    Func<(float Radius, float Height)> AttractionProfile,
    Action<global::Godot.Collections.Array<Rid>> ActorExclusions,
    Action<global::Godot.Collections.Array<Rid>> OcclusionExclusions,
    Func<RuntimeAudioPlaybackIntent, bool> PlayEffect);

/// <summary>
/// One R&C2 scene lease owns its physical crate Bolt pickups. The native
/// payout session decides denominations and collection; this owner only displays
/// admitted pickups and forwards exact actor/pickup identities to the grant port.
/// </summary>
internal sealed class Rac2CrateBoltPresentation(
    RuntimeWorld world, RuntimeWorldScene.Result scene, GodotWorldResources resources, Rac2CrateBoltPorts ports,
    Func<bool> canSpawn)
    : IDisposable
{
    private sealed class PlayerBoltReceipt(ActorInstanceKey actor)
    {
        internal ActorInstanceKey Actor { get; } = actor;
        internal bool Collected;
    }

    private sealed record HostedPickup(Node3D Node, Rac2BoltPickup Pickup, PlayerBoltReceipt? Receipt, Rac2BoltMotionSession Motion)
    {
        internal int? CapacityBlockedAt { get; set; }
    }
    private readonly List<Node3D> _areas = [];
    private readonly List<HostedPickup> _pickups = [];
    private readonly Dictionary<int, List<(ArrayMesh Mesh, Material Material)>> _models = [];
    private Rac2BoltAssets.Catalogue? _assets;
    private WorldMaterialFactory? _materials;
    private Random? _random;
    private double _accumulator;
    private int _emittedRewardIndex;
    private bool _disposed;

    internal void Prepare()
    {
        if (_assets is not null) return;
        string path = ports.SourcePath() ?? throw new InvalidDataException("R&C2 crate pickup source is unavailable.");
        _assets = Rac2BoltAssets.Load(path, world.LevelId);
        _materials = new(world with { Textures = _assets.Textures });
        _random = new(ports.Seed());
    }

    internal void Spawn(Vector3 centre, Rac2CratePayout payout)
    {
        // The owning module admits only a valid R&C2 source, including class-500
        // actors in a mixed-source world and pre-publication diagnostic auto-strikes.
        if (_disposed || !canSpawn()) return;
        Prepare();
        int count = payout.PhysicalPickups.Count;
        for (int i = 0; i < count; i++)
        {
            var pickup = payout.PhysicalPickups[i];
            int nativeClass = Rac2BoltAssets.ClassForDenomination(pickup.Denomination);
            var area = CreateModel(nativeClass, $"Rac2RetailBolt_{pickup.PickupId}_class{nativeClass}");
            var motion = new Rac2BoltMotionSession(nativeClass, ToNative(centre), Sample);
            scene.Root.AddChild(area);
            area.GlobalPosition = FromNative(motion.Position);
            _areas.Add(area);
            PlayerBoltReceipt? receipt = null;
            if (ports.HasNativePlayer())
            {
                int index = checked((world.DynamicObjects?.Select(source => source.InstanceIndex)
                    .DefaultIfEmpty(0).Max() ?? 0) + ++_emittedRewardIndex);
                receipt = new(ports.ActorKey(index, nativeClass));
                var owned = receipt;
                ports.RegisterActor(owned.Actor, $"crate:{payout.Uid}:bolt:{pickup.PickupId}",
                    $"uid:{payout.Uid}", () => !owned.Collected && !_disposed);
            }
            _pickups.Add(new(area, pickup, receipt, motion));
        }
    }

    private float Sample(float minimum, float maximum) => minimum + _random!.NextSingle() * (maximum - minimum);
    private static System.Numerics.Vector3 ToNative(Vector3 point) => new(-point.X, point.Z, point.Y);
    private static Vector3 FromNative(System.Numerics.Vector3 point) => new(-point.X, point.Z, point.Y);

    private Node3D CreateModel(int nativeClass, string name)
    {
        if (!_models.TryGetValue(nativeClass, out var cached))
        {
            cached = [];
            foreach (var surface in _assets!.Models[nativeClass].Surfaces)
            {
                var vertices = Enumerable.Range(0, surface.Positions.Length / 3)
                    .Select(i => RuntimeWorldScene.ToScene(surface.Positions[i * 3], surface.Positions[i * 3 + 1], surface.Positions[i * 3 + 2])).ToArray();
                var uvs = Enumerable.Range(0, surface.Uvs.Length / 2).Select(i => new Vector2(surface.Uvs[i * 2], surface.Uvs[i * 2 + 1])).ToArray();
                int[] indices = (int[])surface.Indices.Clone();
                for (int at = 0; at < indices.Length; at += 3) (indices[at + 1], indices[at + 2]) = (indices[at + 2], indices[at + 1]);
                var arrays = new global::Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = vertices;
                arrays[(int)Mesh.ArrayType.TexUV] = uvs;
                arrays[(int)Mesh.ArrayType.Index] = indices;
                var mesh = new ArrayMesh();
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                cached.Add((mesh, _materials!.Instanced("moby", surface.TextureId, false, surface.MaterialPresentation)));
            }
            _models.Add(nativeClass, cached);
        }
        var node = new Node3D { Name = name, Scale = Vector3.One * Rac2BoltAssets.VisualScale };
        foreach (var surface in cached) node.AddChild(new MeshInstance3D { Mesh = surface.Mesh, MaterialOverride = surface.Material });
        return node;
    }

    internal void Tick(double delta)
    {
        if (_disposed || !canSpawn() || !ports.Current() || ports.Player() is not { } player || !GodotObject.IsInstanceValid(player)) return;
        _accumulator = Math.Min(.2, _accumulator + Math.Clamp(delta, 0, .25));
        int ticks = Math.Min(12, (int)Math.Floor(_accumulator * 60));
        _accumulator -= ticks / 60d;
        var space = player.GetWorld3D().DirectSpaceState;
        var excluded = new global::Godot.Collections.Array<Rid> { player.GetRid() };
        ports.ActorExclusions(excluded);
        for (int tick = 0; tick < ticks; tick++)
        {
            var profile = ports.AttractionProfile();
            for (int index = _pickups.Count - 1; index >= 0; index--)
            {
                var pickup = _pickups[index];
                if (pickup.CapacityBlockedAt == ports.WalletBalance()) continue;
                var motion = pickup.Motion;
                // The selected player's dimensions own attraction admission.
                // Feet-to-socket mapping is an explicit host adapter.
                motion.Step(ToNative(player.GlobalPosition), ToNative(player.GlobalPosition + Vector3.Up * .7f),
                    profile.Radius, profile.Height, ports.Alive(), ProbeContact(space, excluded, motion));
                pickup.Node.GlobalPosition = FromNative(motion.Position);
                var orientation = motion.Orientation;
                pickup.Node.Basis = new Basis(new Quaternion(-orientation.X, orientation.Z, orientation.Y, orientation.W))
                    .Scaled(Vector3.One * Rac2BoltAssets.VisualScale);
                if (!motion.Collected) continue;
                CollectArrivedPickup(player, pickup);
                if (pickup.Node.IsQueuedForDeletion()) _pickups.RemoveAt(index);
            }
        }
    }

    private void CollectArrivedPickup(PlayerHost player, HostedPickup pickup)
    {
        int balance = ports.WalletBalance();
        if (ports.HasNativePlayer() && (long)balance + pickup.Pickup.Denomination > int.MaxValue)
        {
            // Keep the exact receipt, but retire its physical presentation and
            // retry only after capacity changes. Never probe or recollect it.
            pickup.CapacityBlockedAt = balance;
            pickup.Node.Visible = false;
            resources.CrateDiagnostics.Probe.RewardStatus = "pickup receipt waiting for wallet capacity";
            return;
        }
        OnPickupEntered(player, pickup.Node, pickup.Pickup, pickup.Receipt);
    }

    private static Rac2BoltContact? ProbeContact(PhysicsDirectSpaceState3D space,
        global::Godot.Collections.Array<Rid> excluded, Rac2BoltMotionSession motion)
    {
        if (motion.NativeState != 1 || motion.Velocity.Z >= 0) return null;
        Vector3 from = FromNative(motion.Position), next = FromNative(motion.Position + motion.Velocity);
        var query = PhysicsRayQueryParameters3D.Create(from + Vector3.Up * .25f, next - Vector3.Up * .25f);
        query.Exclude = excluded;
        var hit = space.IntersectRay(query);
        return hit.TryGetValue("position", out var position) && hit.TryGetValue("normal", out var normal)
            ? new(ToNative(position.AsVector3()), ToNative(normal.AsVector3())) : null;
    }

    private void OnPickupEntered(Node3D body, Node3D area, Rac2BoltPickup pickup, PlayerBoltReceipt? receipt)
    {
        if (_disposed || !canSpawn() || !ports.Current() || !ports.Alive() || body != ports.Player() ||
            !GodotObject.IsInstanceValid(area) || area.IsQueuedForDeletion() ||
            receipt is not null && (receipt.Collected || !ports.ActorCurrent(receipt.Actor)))
            return;

        var rewards = resources.Rac2CrateBolts;
        if (ports.HasNativePlayer() && (long)ports.WalletBalance() + pickup.Denomination > int.MaxValue) return;
        int denomination;
        try { denomination = rewards.CollectPickup(pickup.PickupId, amount => TryCreditPickup(receipt, pickup.PickupId, amount)); }
        catch (InvalidOperationException) { return; }
        if (denomination == 0) return;
        if (receipt is not null) ports.RefreshHud();
        area.QueueFree();
        _areas.Remove(area);
        ports.PickupAudio(Rac2BoltAssets.ClassForDenomination(pickup.Denomination));
        resources.CrateDiagnostics.Probe.RewardStatus = $"collected +{denomination}; session total {rewards.CollectedBolts}; " +
            $"{rewards.OutstandingPickupCount} physical remain, {rewards.DeferredBolts} deferred";
        GD.Print($"[crate-bolts] pickup={pickup.PickupId} denomination={denomination} " +
                 $"collected={rewards.CollectedBolts} deferred={rewards.DeferredBolts}");
    }

    // The reward intake requires an admitted collection. Make that state
    // provisional until wallet credit succeeds, then commit the native receipt.
    private bool TryCreditPickup(PlayerBoltReceipt? receipt, int pickupId, int denomination)
    {
        if (!ports.HasNativePlayer()) return true;
        if (receipt is null) return false;
        bool accepted = false;
        receipt.Collected = true;
        try
        {
            string? rejection = ports.Credit(receipt.Actor, pickupId, denomination);
            if (rejection is not null)
            {
                resources.CrateDiagnostics.Probe.RewardStatus = $"pickup credit waiting: {rejection}";
                return false;
            }
            accepted = true;
            return true;
        }
        finally { if (!accepted) receipt.Collected = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var area in _areas)
        {
            if (!GodotObject.IsInstanceValid(area) || area.IsQueuedForDeletion()) continue;
            area.QueueFree();
        }
        _areas.Clear();
        _pickups.Clear();
        _models.Clear();
        _materials = null;
        _assets = null;
    }
}
