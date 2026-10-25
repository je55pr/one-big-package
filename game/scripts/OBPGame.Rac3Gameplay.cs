using OBP.Core;
using Godot;
using OBP.Godot;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.Runtime;

namespace OBP.Game;

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
    private const float Rac3Class500HostContactRange = 6.0f;
    private const float Rac3Class500HostContactRadius = 2.4f;
    private const float Rac3Class500HostVerticalTolerance = 3.0f;
    private const uint Rac3Class500HostDamageFlags = 0x00000001;
    private const double Rac3Class500HostDamageHp = 1d;

    private Rac3MobyRuntimeSession _rac3MobyRuntime = new();
    private Rac3DamageTransportSession _rac3DamageTransport = new();
    private Rac3WorldGameplayContext? _rac3GameplayContext;
    private readonly List<RuntimeWorldScene.DynamicObjectNode> _rac3Class500Nodes = [];
    private readonly List<Rac3Class5821AuditSource> _rac3Class5821AuditSources = [];
    private int _rac3AuthoredClass500;
    private int _rac3AdmittedClass500;
    private int _rac3DestroyedClass500;
    private int _rac3AuthoredClass5821;
    private int _rac3AdmittedClass5821;
    private int _rac3PrimaryRatchetSeeded;
    private int _rac3SecondaryRatchetSeeded;
    private int _rac3PrimaryTargetAuditUnknown;
    private int _rac3SecondaryTargetAuditUnknown;
    private readonly DiagnosticSnapshotGate _rac3TargetAudit = new(0.75d);
    private int _rac3TargetAuditQueries;
    private int _rac3TargetAuditSnapshots;
    private string _rac3GameplayStatus = "off";
    private void ResetRac3Gameplay()
    {
        _rac3Class500Nodes.Clear();
        _rac3Class5821AuditSources.Clear();
        _rac3MobyRuntime = new Rac3MobyRuntimeSession();
        _rac3DamageTransport = new Rac3DamageTransportSession();
        _rac3GameplayContext = null;
        _rac3AuthoredClass500 = 0;
        _rac3AdmittedClass500 = 0;
        _rac3DestroyedClass500 = 0;
        _rac3AuthoredClass5821 = 0;
        _rac3AdmittedClass5821 = 0;
        _rac3PrimaryRatchetSeeded = 0;
        _rac3SecondaryRatchetSeeded = 0;
        _rac3PrimaryTargetAuditUnknown = 0;
        _rac3SecondaryTargetAuditUnknown = 0;
        _rac3TargetAudit.Reset();
        _rac3TargetAuditQueries = 0;
        _rac3TargetAuditSnapshots = 0;
        _rac3GameplayStatus = "off";
    }

    private void ConfigureRac3Gameplay(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        ResetRac3Gameplay();
        if (world.Game != ObpSourceGame.Rac3)
            return;

        if (!Rac3WorldGameplaySidecar.TryGet(
                world,
                out Rac3WorldGameplayContext? gameplayContext) ||
            gameplayContext is null)
        {
            _rac3GameplayStatus =
                "blocked: RAC3 production gameplay sidecar missing";
            GD.PushError($"[uya-gameplay] {_rac3GameplayStatus}");
            return;
        }

        _rac3GameplayContext = gameplayContext;
        _rac3MobyRuntime.RegisterWorld(world);
        _ = new Rac3Class500DestructibleSession(_rac3MobyRuntime);
        _ = new Rac3Class5821DamageSession(_rac3MobyRuntime);

        int rejected = 0;
        int unpresented = 0;
        foreach (var source in (world.DynamicObjects ?? Array.Empty<RuntimeDynamicObject>())
            .Where(source => source.NativeClassId == Rac3Class500Destructible.NativeClassId))
        {
            _rac3AuthoredClass500++;
            try
            {
                var authored = Rac3Class500Destructible.ReadAuthored(source);
                if (Rac3Class500Destructible.PostBreakRoute(authored) !=
                    Rac3Class500PostBreakRoute.Deactivate)
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
            _rac3AdmittedClass500++;
            var node = FindPresentedDynamic(result, source);
            if (node is null)
            {
                unpresented++;
                continue;
            }

            _rac3Class500Nodes.Add(node);
        }

        int rejected5821 = 0;
        foreach (var source in (world.DynamicObjects ?? Array.Empty<RuntimeDynamicObject>())
            .Where(source => source.NativeClassId == Rac3Class5821Actor.NativeClassId))
        {
            _rac3AuthoredClass5821++;
            if (!Rac3Class5821Admission.TryRead(source, out var admission, out string? rejection))
            {
                rejected5821++;
                GD.Print($"[uya-gameplay] class-5821 instance {source.InstanceIndex} rejected: {rejection}");
                continue;
            }

            _rac3Class5821AuditSources.Add(
                new Rac3Class5821AuditSource(
                    source,
                    admission.Authored,
                    admission.Placement));
            _rac3AdmittedClass5821++;
        }

        _rac3GameplayStatus =
            $"ready: {_rac3AdmittedClass500}/{_rac3AuthoredClass500} class-500; " +
            $"{_rac3AdmittedClass5821}/{_rac3AuthoredClass5821} class-5821 profiles admitted; " +
            $"{gameplayContext.Gameplay.TargetGroups.Count} target groups / " +
            $"{gameplayContext.Gameplay.TargetPolygons.Count} polygons / " +
            $"{gameplayContext.Gameplay.TargetVolumes.Count} volumes sidecar";
        GD.Print(
            $"[uya-gameplay] {_rac3GameplayStatus}; {_rac3Class500Nodes.Count} class-500 presented, " +
            $"{unpresented} unpresented, {rejected} class-500 rejected, " +
            $"{rejected5821} class-5821 rejected");
    }

    private void ArmRac3Gameplay(PlayerHost player)
    {
        if (OrdinaryPlayerSession || _rac3Class500Nodes.Count == 0)
            return;

        // This is the existing non-R&C1 primary-action host seam. It provides
        // contact facts only; no R&C1 or GC crate consequence is reused here.
        player.CrateStrikeRequested += OnRac3PrimaryActionRequested;
    }

    private void OnRac3PrimaryActionRequested()
    {
        var target = SelectRac3Class500HostContact();
        if (target is null)
        {
            _rac3GameplayStatus = "action: no recovered class-500 destructible in host contact";
            return;
        }

        var damage = new Rac3GameplayDamageEvent(
            Rac3GameplayEntityRef.Player,
            Rac3GameplayEntityRef.Moby(new Rac3MobyRuntimeKey(
                target.Source.NativeClassId,
                target.Source.InstanceIndex)),
            nativeDamage: Rac3Class500HostDamageHp,
            nativeDamageFlags: Rac3Class500HostDamageFlags);
        var dispatch = _rac3DamageTransport.Publish(damage);
        if (!_rac3MobyRuntime.TryDispatchDamage<Rac3Class500BreakResult>(
                damage,
                out var result) ||
            result is null)
        {
            _rac3GameplayStatus =
                $"action: class-500 i{target.Source.InstanceIndex} rejected recovered damage";
            GD.Print($"[uya-gameplay] {_rac3GameplayStatus}");
            return;
        }

        target.ApplyState(result.EntityState);
        _rac3DestroyedClass500++;
        _rac3GameplayStatus =
            $"class-500 i{target.Source.InstanceIndex}: native state " +
            $"{result.NativeStateBefore} -> {result.NativeBreakTransitionState} -> " +
            $"{result.PostBreakRoute}; damage event #{dispatch.Sequence}";
        GD.Print($"[uya-gameplay] {_rac3GameplayStatus}");
    }

    private RuntimeWorldScene.DynamicObjectNode? SelectRac3Class500HostContact()
    {
        if (_player is null || _sceneResult is null || _world?.Game != ObpSourceGame.Rac3)
            return null;

        Vector3 forward = -_player.Camera.GlobalTransform.Basis.Z;
        forward.Y = 0f;
        if (forward.LengthSquared() <= 1e-6f)
            return null;
        forward = forward.Normalized();

        RuntimeWorldScene.DynamicObjectNode? best = null;
        float bestScore = float.PositiveInfinity;
        foreach (var node in _rac3Class500Nodes)
        {
            if (!IsInstanceValid(node.Root) || !node.Root.Visible ||
                !_rac3MobyRuntime.Require(node.Source).IsActive)
                continue;

            Vector3 offset = node.Root.GlobalPosition - _player.GlobalPosition;
            float vertical = Math.Abs(offset.Y);
            offset.Y = 0f;
            float along = offset.Dot(forward);
            if (along <= 0f || along > Rac3Class500HostContactRange ||
                vertical > Rac3Class500HostVerticalTolerance)
                continue;

            float perpendicular = (offset - forward * along).Length();
            if (perpendicular > Rac3Class500HostContactRadius)
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

    private void TickRac3Gameplay(double delta)
    {
        if (_world?.Game != ObpSourceGame.Rac3 ||
            _player is null ||
            _rac3GameplayContext is null ||
            _rac3Class5821AuditSources.Count == 0 ||
            !_rac3TargetAudit.Tick(delta))
        {
            return;
        }

        // One diagnostic snapshot after entry settles, or on inspector request.
        // No native actor update or damage/controller clock is gated here.
        Rac3NativePoint ratchet = new(
            -_player.GlobalPosition.X,
            _player.GlobalPosition.Z,
            _player.GlobalPosition.Y);

        int primarySeeded = 0;
        int secondarySeeded = 0;
        int primaryUnknown = 0;
        int secondaryUnknown = 0;

        foreach (Rac3Class5821AuditSource audit in _rac3Class5821AuditSources)
        {
            _rac3TargetAuditQueries += 2;
            if (!TryAuditRac3RatchetSeed(
                    audit,
                    ratchet,
                    Rac3Class5821Actor.NativeObservedSelectorSubtypePrimary,
                    out bool primarySelected))
            {
                primaryUnknown++;
            }
            else if (primarySelected)
            {
                primarySeeded++;
            }

            if (!TryAuditRac3RatchetSeed(
                    audit,
                    ratchet,
                    Rac3Class5821Actor.NativeObservedSelectorSubtypeSecondaryA,
                    out bool secondarySelected))
            {
                secondaryUnknown++;
            }
            else if (secondarySelected)
            {
                secondarySeeded++;
            }
        }

        _rac3PrimaryRatchetSeeded = primarySeeded;
        _rac3SecondaryRatchetSeeded = secondarySeeded;
        _rac3PrimaryTargetAuditUnknown = primaryUnknown;
        _rac3SecondaryTargetAuditUnknown = secondaryUnknown;

        _rac3TargetAuditSnapshots++;
        GD.Print(
                $"[uya-gameplay] class-5821 Ratchet-seed audit: " +
                $"primary={primarySeeded}/{_rac3Class5821AuditSources.Count}, " +
                $"secondary={secondarySeeded}/{_rac3Class5821AuditSources.Count}, " +
                $"unknown={primaryUnknown}/{secondaryUnknown}; snapshots={_rac3TargetAuditSnapshots}, queries={_rac3TargetAuditQueries}; " +
                "live subtype +0x95 not host-owned");
    }

    private bool TryAuditRac3RatchetSeed(
        Rac3Class5821AuditSource audit,
        Rac3NativePoint ratchet,
        byte nativeSubtype,
        out bool selected)
    {
        selected = false;
        if (_rac3GameplayContext is null)
            return false;

        Rac3Class5821AuthoredState authored = audit.Authored;
        Rac3Class5821TargetSelectionRequest request;
        try
        {
            request = Rac3Class5821Actor.BuildTable1TargetSelectionRequest(
                authored,
                nativeSubtype,
                runtimeModeEnabled: authored.InitialTargetSelectorMode != 0,
                runtimeAuxEnabled: authored.InitialTargetSelectorAux != 0);
        }
        catch (NotSupportedException)
        {
            return false;
        }

        bool eligible;
        Rac3MobyPlacementFacts placement = audit.Placement;
        if (request.NativeSelectorIndex >= 0)
        {
            if (!_rac3GameplayContext.Gameplay.TryContainsTargetGroup(
                    request.NativeSelectorIndex,
                    ratchet.X,
                    ratchet.Y,
                    ratchet.Z,
                    out eligible))
            {
                return false;
            }
        }
        else
        {
            Rac3Class5821RadiusHeightFacts facts =
                Rac3Class5821TargetSelector.BuildNativeRadiusHeightFacts(
                    placement.NativeX,
                    placement.NativeY,
                    placement.NativeZ,
                    ratchet.X,
                    ratchet.Y,
                    ratchet.Z);
            eligible =
                Rac3Class5821TargetSelector.IsNativeRadiusHeightEligible(
                    facts,
                    request);
        }

        Rac3Class5821TargetScoreFacts scoreFacts =
            Rac3Class5821TargetSelector.BuildNativeScoreFacts(
                placement.NativeX,
                placement.NativeY,
                placement.RotationZ,
                ratchet.X,
                ratchet.Y,
                isRatchet: true);
        var ratchetFacts = new Rac3Class5821TargetCandidateFacts(
            Rac3GameplayEntityRef.Player,
            IsRatchet: true,
            NativeRegistryTag: null,
            IsEligible: eligible,
            scoreFacts.HorizontalDistance,
            scoreFacts.ShortestHeadingErrorRadians);

        Rac3Class5821TargetSelectionResult? result =
            Rac3Class5821TargetSelector.SelectOrdinaryTarget(
                request,
                ratchetFacts,
                registryCandidates: []);
        selected = result is { IsRatchet: true };
        return true;
    }

    private sealed record Rac3Class5821AuditSource(
        RuntimeDynamicObject Source,
        Rac3Class5821AuthoredState Authored,
        Rac3MobyPlacementFacts Placement);

    private string GetRac3GameplayHudLine()
    {
        if (_world?.Game != ObpSourceGame.Rac3 || _rac3GameplayStatus == "off")
            return string.Empty;

        int live = _rac3Class500Nodes.Count(node =>
            IsInstanceValid(node.Root) &&
            _rac3MobyRuntime.Require(node.Source).IsActive);
        return
            $"UYA TABLE1 slice: X / west-face action uses temporary host contact; " +
            $"UYA-native class-500 damage/lifecycle\n" +
            $"class-500 authored {_rac3AuthoredClass500}, admitted {_rac3AdmittedClass500}, " +
            $"presented live {live}, destroyed {_rac3DestroyedClass500}\n" +
            $"class-5821 profiles {_rac3AdmittedClass5821}/{_rac3AuthoredClass5821}; " +
            $"Ratchet seed snapshot (entry +0.75s; I refresh) primary {_rac3PrimaryRatchetSeeded}, secondary {_rac3SecondaryRatchetSeeded}, " +
            $"unknown {_rac3PrimaryTargetAuditUnknown}/{_rac3SecondaryTargetAuditUnknown}\n" +
            $"live subtype +0x95 / runtime AI state / player consequence pending   {_rac3GameplayStatus}";
    }
}
