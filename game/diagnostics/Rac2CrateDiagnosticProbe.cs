using System.Numerics;
using OBP.RAC2.Gameplay;

namespace OBP.Game.Diagnostics;

public sealed record Rac2CrateTargetSnapshot(string InteractionId, int? NativeUid, int InstanceIndex,
    Vector3 Position, bool Valid, bool Visible);
public sealed record Rac2CrateRewardSnapshot(int CollectedBolts, int OutstandingPickups, int DeferredBolts);

// Property names and native damage values retain the established capture sidecar contract.
public sealed record Rac2CrateDebugSnapshot(string? TargetInteractionId, int? NativeUid, int? NativeInstanceIndex,
    bool Visible, bool Broken, string Status, string EventFlags, float EventScalar, byte? PvarC8,
    string? Route, string RewardStatus, int CollectedBolts, int OutstandingPickups, int DeferredBolts);

/// <summary>World-local diagnostic observations and explicit focus controls. Never owns native reward state.</summary>
public sealed class Rac2CrateDiagnosticProbe
{
    public bool FocusRequested { get; private set; }
    public bool AutoStrike { get; private set; }
    public int? TargetIndex { get; private set; }
    public string Status { get; set; } = "off";
    public string RewardStatus { get; set; } = "off";
    public byte? PvarC8 { get; private set; }
    public string? Route { get; private set; }
    public bool Broken { get; set; }

    public void Configure(bool focus, bool autoStrike, Rac2CrateTargetSnapshot? target)
    {
        FocusRequested = focus || autoStrike;
        AutoStrike = autoStrike;
        TargetIndex = FocusRequested ? target?.InstanceIndex : null;
        Status = "off";
        if (FocusRequested) Status = target is null ? "requested: no class-500 target" : $"ready: {target.InteractionId}";
    }
    public void ObserveStrike(int instanceIndex, byte pvarC8, string route, string source)
    {
        TargetIndex = instanceIndex;
        PvarC8 = pvarC8;
        Route = route;
        Status = $"{source}: state 1 -> 3 -> {route}";
    }
    public bool TryGetFocusPose(Rac2CrateTargetSnapshot? target, Vector3? ship, out Vector3 spawn, out float yaw)
    {
        spawn = default;
        yaw = 0;
        if (!FocusRequested || target is not { Valid: true }) return false;
        Vector3 centre = target.Position;
        Vector3 approach = Vector3.UnitZ;
        if (ship is { } position)
        {
            var fromCrateToShip = position - centre;
            fromCrateToShip.Y = 0;
            if (fromCrateToShip.LengthSquared() > 1) approach = Vector3.Normalize(fromCrateToShip);
        }
        spawn = centre + approach * 7 + Vector3.UnitY * 4;
        var look = centre - spawn;
        look.Y = 0;
        yaw = look.LengthSquared() > .01f ? MathF.Atan2(look.X, look.Z) + MathF.PI : 0;
        return true;
    }
    public string HudLine(Rac2CrateTargetSnapshot? target, Rac2CrateRewardSnapshot rewards) => Status == "off" ? string.Empty :
        $"Crate debug: {target?.InteractionId ?? "none"}   {Status}\n" +
        $"Bolt payout: {RewardStatus}   collected {rewards.CollectedBolts}   " +
        $"outstanding {rewards.OutstandingPickups}   deferred {rewards.DeferredBolts}";
    public Rac2CrateDebugSnapshot? Capture(Rac2CrateTargetSnapshot? target, Rac2CrateRewardSnapshot rewards) => Status == "off" ? null :
        new(target?.InteractionId, target?.NativeUid, target?.InstanceIndex, target is { Valid: true, Visible: true }, Broken,
            Status, $"0x{Rac2PlayerAttackDamage.State20.DamageFlags:X8}", Rac2PlayerAttackDamage.State20.DamageHp,
            PvarC8, Route, RewardStatus, rewards.CollectedBolts, rewards.OutstandingPickups, rewards.DeferredBolts);
}
