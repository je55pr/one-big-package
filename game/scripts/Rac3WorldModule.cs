using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// Original R&C3 TABLE1 gameplay for one world lease. Godot contributes temporary
/// contact geometry; the versioned R&C3 native sessions decide damage and lifecycle.
/// No source-world decision here changes the persistent player equipment or health.
/// </summary>
internal sealed class Rac3WorldModule : IDisposable
{
    // Temporary host envelope, NOT native R&C3 attack reach.
    private const float HostContactRange = 6f;
    private const float HostContactRadius = 2.4f;
    private const float HostVerticalTolerance = 3f;
    private const uint NativeDamageFlags = 0x00000001;
    private const double NativeDamageHp = 1d;

    private readonly RuntimeWorld _world;
    private readonly RuntimeWorldScene.Result _scene;
    private readonly List<RuntimeWorldScene.DynamicObjectNode> _class500 = [];
    private Rac3Class500WorldRuntime? _class500World;
    private PlayerHost? _player;
    private Rac3WorldGameplayContext? _gameplay;
    private bool _configured;
    private bool _subscribed;
    private bool _disposed;
    private int _destroyed500;
    private int _authored5821;
    private int _admitted5821;
    private string _status = "off";

    // Source gating lives with the source module. World activation must not
    // select player or gameplay ownership through an origin-dependent branch.
    internal static Rac3WorldModule? CreateForWorld(RuntimeWorld world, RuntimeWorldScene.Result scene)
    {
        // Only the R&C3 importer attaches this source-qualified context.
        if (Rac3WorldGameplaySidecar.TryGet(world, out Rac3WorldGameplayContext? gameplay) && gameplay is not null)
            return new(world, scene);
        if (world.BuildId == Rac3Authority.Primary.BuildId)
            GD.PushError("[uya-gameplay] blocked: RAC3 production gameplay sidecar missing");
        return null;
    }

    internal Rac3WorldModule(RuntimeWorld world, RuntimeWorldScene.Result scene)
    {
        _world = world;
        _scene = scene;
    }

    internal bool Current => !_disposed && _configured && _gameplay is not null && _class500World?.Active == true;
    internal RuntimeWorld World => _world;
    internal bool HasPlayer(PlayerHost player) => Current && ReferenceEquals(_player, player);
    internal IReadOnlyList<RuntimeWorldScene.DynamicObjectNode> OriginalTargets =>
        Current ? _class500 : [];

    internal void Configure()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_configured) throw new InvalidOperationException("R&C3 world is already configured.");
        _configured = true;
        if (!Rac3WorldGameplaySidecar.TryGet(_world, out Rac3WorldGameplayContext? gameplay) ||
            gameplay is null)
        {
            if (_world.BuildId == Rac3Authority.Primary.BuildId)
                GD.PushError("[uya-gameplay] blocked: RAC3 production gameplay sidecar missing");
            return;
        }

        _gameplay = gameplay;
        _class500World = new Rac3Class500WorldRuntime(_world);
        int unpresented = 0;
        foreach (var source in _class500World.AdmittedSources)
        {
            var node = Rac1WorldPresentation.FindPresentedDynamic(_scene, source);
            if (node is null) { unpresented++; continue; }
            _class500.Add(node);
        }
        int rejected5821 = 0;
        foreach (var source in (_world.DynamicObjects ?? Array.Empty<RuntimeDynamicObject>())
            .Where(source => source.NativeClassId == Rac3Class5821Actor.NativeClassId))
        {
            _authored5821++;
            if (!Rac3Class5821Admission.TryRead(source, out _, out string? reason))
            {
                rejected5821++;
                GD.Print($"[uya-gameplay] class-5821 instance {source.InstanceIndex} rejected: {reason}");
                continue;
            }
            _admitted5821++;
        }

        _status =
            $"ready: {_class500World.AdmittedCount}/{_class500World.AuthoredCount} class-500; " +
            $"{_admitted5821}/{_authored5821} class-5821 profiles admitted; " +
            $"{gameplay.Gameplay.TargetGroups.Count} target groups / " +
            $"{gameplay.Gameplay.TargetPolygons.Count} polygons / " +
            $"{gameplay.Gameplay.TargetVolumes.Count} volumes sidecar";
        GD.Print($"[uya-gameplay] {_status}; {_class500.Count} class-500 presented, " +
            $"{unpresented} unpresented, {_class500World.RejectedCount} class-500 rejected, {rejected5821} class-5821 rejected");
    }

    internal void Arm(PlayerHost player, bool ordinaryPlayer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Current || !GodotObject.IsInstanceValid(player)) return;
        if (_player is not null) throw new InvalidOperationException("R&C3 player already armed.");
        _player = player;
        // Existing parity-only contact seam. The original-world source does not grant
        // this action to an ordinary player just because the destination is R&C3.
        if (!ordinaryPlayer && _class500.Count > 0)
        {
            player.CrateStrikeRequested += OnPrimaryAction;
            _subscribed = true;
        }
    }

    private void OnPrimaryAction()
    {
        if (!Current || !GodotObject.IsInstanceValid(_player)) return;
        var target = SelectHostContact();
        if (target is null)
        {
            _status = "action: no recovered class-500 destructible in host contact";
            return;
        }
        if (!_class500World!.TryBreak(target.Source, NativeDamageHp, NativeDamageFlags,
                publish: true, out var result, out long sequence))
        {
            _status = $"action: class-500 i{target.Source.InstanceIndex} rejected recovered damage";
            GD.Print($"[uya-gameplay] {_status}");
            return;
        }
        // Authored world dynamic nodes have no ActorSceneFactory contact body;
        // ordinary placed actors disable their separately owned bodies via the actor bridge.
        target.ApplyState(result.EntityState);
        _destroyed500++;
        _status = $"class-500 i{target.Source.InstanceIndex}: native state " +
            $"{result.NativeStateBefore} -> {result.NativeBreakTransitionState} -> " +
            $"{result.PostBreakRoute}; damage event #{sequence}";
        GD.Print($"[uya-gameplay] {_status}");
    }

    private RuntimeWorldScene.DynamicObjectNode? SelectHostContact()
    {
        if (!Current || _player is null || !GodotObject.IsInstanceValid(_player)) return null;
        Vector3 forward = -_player.Camera.GlobalTransform.Basis.Z;
        forward.Y = 0f;
        if (forward.LengthSquared() <= 1e-6f) return null;
        forward = forward.Normalized();

        RuntimeWorldScene.DynamicObjectNode? best = null;
        float bestScore = float.PositiveInfinity;
        foreach (var node in _class500)
        {
            if (!GodotObject.IsInstanceValid(node.Root) || !node.Root.Visible ||
                !_class500World!.IsActive(node.Source)) continue;
            Vector3 offset = node.Root.GlobalPosition - _player.GlobalPosition;
            float vertical = Math.Abs(offset.Y);
            offset.Y = 0f;
            float along = offset.Dot(forward);
            if (along <= 0f || along > HostContactRange || vertical > HostVerticalTolerance) continue;
            float perpendicular = (offset - forward * along).Length();
            if (perpendicular > HostContactRadius) continue;
            float score = (perpendicular * 10f) + along + vertical;
            if (score < bestScore) { bestScore = score; best = node; }
        }
        return best;
    }

    // Used only by original-world actor registration. The actor bridge captures THIS
    // module rather than resolving a mutable current-world native session later.
    internal ActorObservation Observe(RuntimeWorldScene.DynamicObjectNode node)
    {
        if (!Owns(node) || !GodotObject.IsInstanceValid(node.Root))
            return new(System.Numerics.Vector3.Zero, false, true, null, 0, null);
        var active = _class500World!.IsActive(node.Source);
        return new(new(node.Root.GlobalPosition.X, node.Root.GlobalPosition.Y, node.Root.GlobalPosition.Z),
            active, !active, null,
            active ? Rac3Class500Destructible.ActiveNativeState :
                Rac3Class500Destructible.BreakTransitionNativeState, null);
    }

    internal ActorHitResult Apply(RuntimeWorldScene.DynamicObjectNode node,
        WeaponDamageIntent intent, Action disableBody)
    {
        if (!Owns(node) || !GodotObject.IsInstanceValid(node.Root))
            return new(false, false, "The R&C3 source world or actor has retired.");
        // Source-qualified weapon admission is performed by the shared actor set.
        // The recovered UYA receiver alone decides native class-500 damage.
        if (!_class500World!.TryBreak(node.Source, intent.SourceDamage, NativeDamageFlags,
                publish: false, out var result, out _))
            return new(false, false, "Native UYA class-500 receiver rejected this contact.");
        node.ApplyState(result.EntityState);
        disableBody();
        return new(true, true);
    }

    private bool Owns(RuntimeWorldScene.DynamicObjectNode node) =>
        Current && node.Source.SourceGame == ObpSourceGame.Rac3 &&
        _class500.Contains(node);

    internal string HudLine()
    {
        if (!Current || _status == "off") return string.Empty;
        int live = _class500.Count(node => GodotObject.IsInstanceValid(node.Root) &&
            _class500World!.IsActive(node.Source));
        return $"UYA TABLE1 slice: X / west-face action uses temporary host contact; " +
            $"UYA-native class-500 damage/lifecycle\n" +
            $"class-500 authored {_class500World!.AuthoredCount}, admitted {_class500World!.AdmittedCount}, " +
            $"presented live {live}, destroyed {_destroyed500}\n" +
            $"class-5821 profiles {_admitted5821}/{_authored5821}; " +
            $"native target-selection/AI/player consequence pending   {_status}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_subscribed && GodotObject.IsInstanceValid(_player))
            _player!.CrateStrikeRequested -= OnPrimaryAction;
        _subscribed = false;
        _player = null;
        _class500.Clear();
        _class500World?.Dispose();
        _class500World = null;
        _gameplay = null;
        _status = "off";
    }
}
