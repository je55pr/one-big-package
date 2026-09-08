using Godot;
using OBP.Godot;
using OBP.RAC2.Gameplay;
using OBP.Runtime;

namespace OneBigPackage;

/// <summary>
/// Development-only GC crate interaction harness. Target acquisition is a host
/// convenience; the actual class-500 break decision is delegated to the
/// retail-backed RAC2 gameplay rule.
/// </summary>
public partial class OBPGame
{
    private const uint DebugCrateEventFlags = 0x00000001;
    private const float DebugCrateEventScalar = 1f;

    private RuntimeWorldScene.DynamicObjectNode? _crateDebugTarget;
    private string _crateDebugStatus = "off";
    private byte? _crateDebugPvarC8;
    private string? _crateDebugRoute;
    private bool _crateDebugBroken;
    private GcFreshBoltSession _crateBoltSession = new();
    private string _crateRewardStatus = "off";

    private bool CrateDebugRequested => _args.CrateFocus || _args.CrateAutoStrike;

    private void ResetCrateDebugHarness()
    {
        _crateDebugTarget = null;
        _crateDebugStatus = "off";
        _crateDebugPvarC8 = null;
        _crateDebugRoute = null;
        _crateDebugBroken = false;
        _crateBoltSession = new GcFreshBoltSession();
        _crateRewardStatus = "off";
    }
    private void ConfigureCrateDebugHarness()
    {
        ResetCrateDebugHarness();
        if (!CrateDebugRequested || _sceneResult?.DynamicObjectNodes is not { } nodes)
        {
            return;
        }

        _crateDebugTarget = nodes
            .Where(n => n.Source.SourceGame == "rac2" && n.Source.NativeClassId == 500)
            .OrderBy(n => n.Source.InstanceIndex)
            .FirstOrDefault();

        _crateDebugStatus = _crateDebugTarget is null
            ? "requested: no class-500 target"
            : $"ready: {_crateDebugTarget.Source.InteractionId}";

        if (_crateDebugTarget is { } target)
        {
            GD.Print($"[crate-debug] target {target.Source.InteractionId} uid={target.Source.NativeUid?.ToString() ?? "?"}");
        }
    }

    private void ArmCrateDebugHarness(DebugPlayer player)
    {
        player.CrateStrikeRequested += OnDebugCrateStrikeRequested;
        if (_args.CrateAutoStrike && _crateDebugTarget is { } target)
        {
            ApplyDebugCrateStrike(target, "auto");
        }
    }
    private void OnDebugCrateStrikeRequested()
    {
        var target = SelectDebugCrateFromAim();
        if (target is null)
        {
            _crateDebugStatus = "strike: no aimed class-500 crate";
            GD.Print("[crate-debug] strike ignored: no aimed class-500 crate");
            return;
        }

        ApplyDebugCrateStrike(target, "manual");
    }

    private RuntimeWorldScene.DynamicObjectNode? SelectDebugCrateFromAim()
    {
        if (CrateDebugRequested && _crateDebugTarget is { } focused
            && IsInstanceValid(focused.Root) && focused.Root.Visible)
        {
            return focused;
        }

        if (_sceneResult?.DynamicObjectNodes is not { } nodes || _player?.Camera is not { } camera)
        {
            return null;
        }

        Vector3 origin = camera.GlobalPosition;
        Vector3 forward = -camera.GlobalTransform.Basis.Z.Normalized();
        RuntimeWorldScene.DynamicObjectNode? best = null;
        float bestScore = float.PositiveInfinity;
        foreach (var node in nodes)
        {
            if (node.Source.SourceGame != "rac2" || node.Source.NativeClassId != 500
                || !IsInstanceValid(node.Root) || !node.Root.Visible)
            {
                continue;
            }

            Vector3 to = node.Root.GlobalPosition - origin;
            float along = to.Dot(forward);
            if (along <= 0f || along > 45f)
            {
                continue;
            }

            float perpendicular = (to - forward * along).Length();
            if (perpendicular > 4.5f)
            {
                continue;
            }

            float score = perpendicular * 10f + along * 0.02f;
            if (score < bestScore)
            {
                bestScore = score;
                best = node;
            }
        }

        return best;
    }
    private void ApplyDebugCrateStrike(RuntimeWorldScene.DynamicObjectNode target, string source)
    {
        if (!GcCrateInteraction.ShouldBreakClass500(DebugCrateEventFlags, DebugCrateEventScalar))
        {
            throw new InvalidOperationException("The deterministic debug event no longer satisfies the recovered class-500 predicate.");
        }

        var authored = GcClass500Authority.Read(target.Source);
        if (authored is null || authored.PvarC8 is not { } c8)
        {
            _crateDebugStatus = "strike: target has no usable class-500 authority state";
            GD.PrintErr($"[crate-debug] {target.Source.InteractionId}: missing authored UID/Bolts/PVar+C8");
            return;
        }

        var route = GcCrateInteraction.PostBreakRoute(c8);
        _crateDebugTarget = target;
        _crateDebugPvarC8 = c8;
        _crateDebugRoute = route.ToString();
        _crateDebugStatus = $"{source}: state 1 -> 3 -> {route}";

        GcClass500Payout payout;
        try
        {
            // Harness inputs are explicit deterministic choices within recovered
            // native domains, not claims about arbitrary retail save state. A
            // multiplier byte of zero is the neutral native case via max(1, byte).
            payout = _crateBoltSession.PlanClass500Payout(
                authored.Uid, authored.AuthoredBolts, rewardMultiplierByte: 0,
                progressionLikeInput: 0, rngMod2: 1);
        }
        catch (InvalidOperationException ex)
        {
            _crateRewardStatus = ex.Message;
            GD.PrintErr($"[crate-bolts] {target.Source.InteractionId}: {ex.Message}");
            return;
        }

        SpawnCrateBoltPickups(target.Root.GlobalPosition, payout);
        _crateRewardStatus = $"fresh selector {payout.Selector}: centre {payout.RewardCentreValue} => " +
            $"{string.Join("+", payout.PhysicalPickups.Select(p => p.Denomination))} physical, {payout.DeferredValue} deferred";

        // The Oozla class-500 state-3 path immediately enters the native
        // deactivate helper when authored +0xC8 is zero. State 6 remains visible
        // because that class-family behaviour has not yet been reconstructed.
        if (route == GcClass500PostBreakRoute.Deactivate)
        {
            target.Root.Visible = false;
            _crateDebugBroken = true;
        }
        GD.Print($"[crate-debug] {target.Source.InteractionId} event=0x{DebugCrateEventFlags:X8}/{DebugCrateEventScalar:0.###} " +
                 $"PVar+C8={c8} => state {GcCrateInteraction.BreakTransitionState} -> {route}");
    }

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
            EventFlags: $"0x{DebugCrateEventFlags:X8}",
            EventScalar: DebugCrateEventScalar,
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
