using Godot;
using OBP.Godot;
using OBP.RAC3.Gameplay;
using OBP.Runtime;

namespace OneBigPackage;

/// <summary>
/// Thin host bridge for the currently recovered UYA TABLE1 gameplay slice.
/// Godot supplies temporary player-action contact admission; UYA runtime code
/// owns authored identity, native damage admission, and lifecycle consequences.
/// </summary>
public partial class OBPGame
{
    // Temporary host contact envelope only. These dimensions are not claimed as
    // native UYA wrench/weapon reach. The damage record itself is admitted by
    // the recovered TABLE1 class-500 routine.
    private const float UyaClass500HostContactRange = 6.0f;
    private const float UyaClass500HostContactRadius = 2.4f;
    private const float UyaClass500HostVerticalTolerance = 3.0f;
    private const uint UyaClass500HostDamageFlags = 0x00000001;
    private const double UyaClass500HostDamageHp = 1d;

    private UyaMobyRuntimeSession _uyaMobyRuntime = new();
    private UyaDamageTransportSession _uyaDamageTransport = new();
    private readonly List<RuntimeWorldScene.DynamicObjectNode> _uyaClass500Nodes = [];
    private int _uyaAuthoredClass500;
    private int _uyaAdmittedClass500;
    private int _uyaDestroyedClass500;
    private string _uyaGameplayStatus = "off";
    private void ResetUyaGameplay()
    {
        _uyaClass500Nodes.Clear();
        _uyaMobyRuntime = new UyaMobyRuntimeSession();
        _uyaDamageTransport = new UyaDamageTransportSession();
        _uyaAuthoredClass500 = 0;
        _uyaAdmittedClass500 = 0;
        _uyaDestroyedClass500 = 0;
        _uyaGameplayStatus = "off";
    }

    private void ConfigureUyaGameplay(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        ResetUyaGameplay();
        if (world.Game != "rac3")
            return;

        _uyaMobyRuntime.RegisterWorld(world);
        _ = new UyaClass500DestructibleSession(_uyaMobyRuntime);

        int rejected = 0;
        int unpresented = 0;
        foreach (var source in (world.DynamicObjects ?? Array.Empty<RuntimeDynamicObject>())
            .Where(source => source.NativeClassId == UyaClass500Destructible.NativeClassId))
        {
            _uyaAuthoredClass500++;
            try
            {
                var authored = UyaClass500Destructible.ReadAuthored(source);
                if (UyaClass500Destructible.PostBreakRoute(authored) !=
                    UyaClass500PostBreakRoute.Deactivate)
                {
                    rejected++;
                    continue;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
            {
                rejected++;
                continue;
            }
            _uyaAdmittedClass500++;
            var node = FindPresentedDynamic(result, source);
            if (node is null)
            {
                unpresented++;
                continue;
            }

            _uyaClass500Nodes.Add(node);
        }

        _uyaGameplayStatus =
            $"ready: {_uyaAdmittedClass500}/{_uyaAuthoredClass500} authored class-500 destructibles admitted";
        GD.Print(
            $"[uya-gameplay] {_uyaGameplayStatus}; {_uyaClass500Nodes.Count} presented, " +
            $"{unpresented} unpresented, {rejected} rejected");
    }

    private void ArmUyaGameplay(DebugPlayer player)
    {
        if (_world?.Game != "rac3")
            return;

        // This is the existing non-R&C1 primary-action host seam. It provides
        // contact facts only; no R&C1 or GC crate consequence is reused here.
        player.CrateStrikeRequested += OnUyaPrimaryActionRequested;
    }

    private void OnUyaPrimaryActionRequested()
    {
        var target = SelectUyaClass500HostContact();
        if (target is null)
        {
            _uyaGameplayStatus = "action: no recovered class-500 destructible in host contact";
            return;
        }

        var damage = new UyaGameplayDamageEvent(
            UyaGameplayEntityRef.Player,
            UyaGameplayEntityRef.Moby(new UyaMobyRuntimeKey(
                target.Source.NativeClassId,
                target.Source.InstanceIndex)),
            nativeDamage: UyaClass500HostDamageHp,
            nativeDamageFlags: UyaClass500HostDamageFlags);
        var dispatch = _uyaDamageTransport.Publish(damage);
        if (!_uyaMobyRuntime.TryDispatchDamage<UyaClass500BreakResult>(
                damage,
                out var result) ||
            result is null)
        {
            _uyaGameplayStatus =
                $"action: class-500 i{target.Source.InstanceIndex} rejected recovered damage";
            GD.Print($"[uya-gameplay] {_uyaGameplayStatus}");
            return;
        }

        target.ApplyState(result.EntityState);
        _uyaDestroyedClass500++;
        _uyaGameplayStatus =
            $"class-500 i{target.Source.InstanceIndex}: native state " +
            $"{result.NativeStateBefore} -> {result.NativeBreakTransitionState} -> " +
            $"{result.PostBreakRoute}; damage event #{dispatch.Sequence}";
        GD.Print($"[uya-gameplay] {_uyaGameplayStatus}");
    }

    private RuntimeWorldScene.DynamicObjectNode? SelectUyaClass500HostContact()
    {
        if (_player is null || _sceneResult is null || _world?.Game != "rac3")
            return null;

        Vector3 forward = -_player.Camera.GlobalTransform.Basis.Z;
        forward.Y = 0f;
        if (forward.LengthSquared() <= 1e-6f)
            return null;
        forward = forward.Normalized();

        RuntimeWorldScene.DynamicObjectNode? best = null;
        float bestScore = float.PositiveInfinity;
        foreach (var node in _uyaClass500Nodes)
        {
            if (!IsInstanceValid(node.Root) || !node.Root.Visible ||
                !_uyaMobyRuntime.Require(node.Source).IsActive)
                continue;

            Vector3 offset = node.Root.GlobalPosition - _player.GlobalPosition;
            float vertical = Math.Abs(offset.Y);
            offset.Y = 0f;
            float along = offset.Dot(forward);
            if (along <= 0f || along > UyaClass500HostContactRange ||
                vertical > UyaClass500HostVerticalTolerance)
                continue;

            float perpendicular = (offset - forward * along).Length();
            if (perpendicular > UyaClass500HostContactRadius)
                continue;

            float score = (perpendicular * 10f) + along + vertical;
            if (score < bestScore)
            {
                bestScore = score;
                best = node;
            }
        }

        return best;
    }

    private string GetUyaGameplayHudLine()
    {
        if (_world?.Game != "rac3" || _uyaGameplayStatus == "off")
            return string.Empty;

        int live = _uyaClass500Nodes.Count(node =>
            IsInstanceValid(node.Root) &&
            _uyaMobyRuntime.Require(node.Source).IsActive);
        return
            $"UYA TABLE1 slice: X / west-face action uses temporary host contact; " +
            $"UYA-native class-500 damage/lifecycle\n" +
            $"class-500 authored {_uyaAuthoredClass500}, admitted {_uyaAdmittedClass500}, " +
            $"presented live {live}, destroyed {_uyaDestroyedClass500}   {_uyaGameplayStatus}";
    }
}
