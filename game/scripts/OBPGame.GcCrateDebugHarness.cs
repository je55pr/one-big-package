using Godot;
using OBP.Godot;
using OBP.RAC2.Gameplay;
using OBP.Runtime;

namespace OneBigPackage;

public partial class OBPGame
{
    private RuntimeWorldScene.DynamicObjectNode? _crateDebugTarget;
    private string _crateDebugStatus = "off";
    private byte? _crateDebugPvarC8;
    private string? _crateDebugRoute;
    private bool _crateDebugBroken;
    private GcFreshBoltSession _crateBoltSession = new();
    private string _crateRewardStatus = "off";

    private bool CrateDebugRequested => _args.CrateFocus || _args.CrateAutoStrike;

    private bool TryGetCrateFocusPose(RuntimeWorld world, out Vector3 spawn, out float yaw)
    {
        spawn = default;
        yaw = 0f;
        if (!CrateDebugRequested || _crateDebugTarget is not { } target || !IsInstanceValid(target.Root))
        {
            return false;
        }

        Vector3 centre = target.Root.GlobalPosition;
        Vector3 approach = Vector3.Back;
        if (world.Ship is { } ship)
        {
            Vector3 fromCrateToShip = RuntimeWorldScene.ToScene(ship.X, ship.Y, ship.Z) - centre;
            fromCrateToShip.Y = 0f;
            if (fromCrateToShip.LengthSquared() > 1f)
            {
                approach = fromCrateToShip.Normalized();
            }
        }

        spawn = centre + approach * 7f + Vector3.Up * 4f;
        Vector3 look = centre - spawn;
        look.Y = 0f;
        yaw = look.LengthSquared() > 0.01f ? Mathf.Atan2(look.X, look.Z) + Mathf.Pi : 0f;
        return true;
    }
    private string GetCrateDebugHudLine()
    {
        if (_crateDebugStatus == "off")
        {
            return string.Empty;
        }

        string target = _crateDebugTarget?.Source.InteractionId ?? "none";
        return $"Crate debug: {target}   {_crateDebugStatus}\n" +
            $"Bolt payout: {_crateRewardStatus}   collected {_crateBoltSession.CollectedBolts}   " +
            $"outstanding {_crateBoltSession.OutstandingPickupCount}   deferred {_crateBoltSession.DeferredBolts}";
    }

    private CrateDebugSnapshot? GetCrateDebugSnapshot()
    {
        if (_crateDebugStatus == "off")
        {
            return null;
        }

        var target = _crateDebugTarget;
        return new CrateDebugSnapshot(
            TargetInteractionId: target?.Source.InteractionId,
            NativeUid: target?.Source.NativeUid,
            NativeInstanceIndex: target?.Source.InstanceIndex,
            Visible: target is { } t && IsInstanceValid(t.Root) && t.Root.Visible,
            Broken: _crateDebugBroken,
            Status: _crateDebugStatus,
            EventFlags: $"0x{GcPlayerAttackDamage.State20.DamageFlags:X8}",
            EventScalar: GcPlayerAttackDamage.State20.DamageHp,
            PvarC8: _crateDebugPvarC8,
            Route: _crateDebugRoute,
            RewardStatus: _crateRewardStatus,
            CollectedBolts: _crateBoltSession.CollectedBolts,
            OutstandingPickups: _crateBoltSession.OutstandingPickupCount,
            DeferredBolts: _crateBoltSession.DeferredBolts);
    }

    private sealed record CrateDebugSnapshot(
        string? TargetInteractionId,
        int? NativeUid,
        int? NativeInstanceIndex,
        bool Visible,
        bool Broken,
        string Status,
        string EventFlags,
        float EventScalar,
        byte? PvarC8,
        string? Route,
        string RewardStatus,
        int CollectedBolts,
        int OutstandingPickups,
        int DeferredBolts);
}
