using Godot;
using OBP.Godot;
using OBP.RAC3;
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
    private Rac3WorldGameplayContext? _uyaGameplayContext;
    private readonly List<RuntimeWorldScene.DynamicObjectNode> _uyaClass500Nodes = [];
    private readonly List<UyaClass5821AuditSource> _uyaClass5821AuditSources = [];
    private int _uyaAuthoredClass500;
    private int _uyaAdmittedClass500;
    private int _uyaDestroyedClass500;
    private int _uyaAuthoredClass5821;
    private int _uyaAdmittedClass5821;
    private int _uyaPrimaryRatchetSeeded;
    private int _uyaSecondaryRatchetSeeded;
    private int _uyaPrimaryTargetAuditUnknown;
    private int _uyaSecondaryTargetAuditUnknown;
    private double _uyaTargetAuditAge;
    private bool _uyaTargetAuditLogged;
    private string _uyaGameplayStatus = "off";
    private void ResetUyaGameplay()
    {
        _uyaClass500Nodes.Clear();
        _uyaClass5821AuditSources.Clear();
        _uyaMobyRuntime = new UyaMobyRuntimeSession();
        _uyaDamageTransport = new UyaDamageTransportSession();
        _uyaGameplayContext = null;
        _uyaAuthoredClass500 = 0;
        _uyaAdmittedClass500 = 0;
        _uyaDestroyedClass500 = 0;
        _uyaAuthoredClass5821 = 0;
        _uyaAdmittedClass5821 = 0;
        _uyaPrimaryRatchetSeeded = 0;
        _uyaSecondaryRatchetSeeded = 0;
        _uyaPrimaryTargetAuditUnknown = 0;
        _uyaSecondaryTargetAuditUnknown = 0;
        _uyaTargetAuditAge = 0d;
        _uyaTargetAuditLogged = false;
        _uyaGameplayStatus = "off";
    }

    private void ConfigureUyaGameplay(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        ResetUyaGameplay();
        if (world.Game != "rac3")
            return;

        if (!Rac3WorldGameplaySidecar.TryGet(
                world,
                out Rac3WorldGameplayContext? gameplayContext) ||
            gameplayContext is null)
        {
            _uyaGameplayStatus =
                "blocked: RAC3 production gameplay sidecar missing";
            GD.PushError($"[uya-gameplay] {_uyaGameplayStatus}");
            return;
        }

        _uyaGameplayContext = gameplayContext;
        _uyaMobyRuntime.RegisterWorld(world);
        _ = new UyaClass500DestructibleSession(_uyaMobyRuntime);
        _ = new UyaClass5821DamageSession(_uyaMobyRuntime);

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

        int rejected5821 = 0;
        foreach (var source in (world.DynamicObjects ?? Array.Empty<RuntimeDynamicObject>())
            .Where(source => source.NativeClassId == UyaClass5821Actor.NativeClassId))
        {
            _uyaAuthoredClass5821++;
            try
            {
                UyaClass5821AuthoredState authored =
                    UyaClass5821Actor.ReadAuthored(source);
                if (!UyaClass5821Actor.HasRecoveredTable1DamageProfile(authored) ||
                    !UyaClass5821Actor.HasRecoveredTable1OrdinaryAttackProfile(authored) ||
                    !UyaClass5821Actor.HasRecoveredTable1TargetSelectionProfile(authored))
                {
                    rejected5821++;
                    continue;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
            {
                rejected5821++;
                continue;
            }

            _uyaAdmittedClass5821++;
            _uyaClass5821AuditSources.Add(
                new UyaClass5821AuditSource(
                    source,
                    UyaClass5821Actor.ReadAuthored(source),
                    UyaMobyPlacement.ReadAuthored(source)));
        }

        _uyaGameplayStatus =
            $"ready: {_uyaAdmittedClass500}/{_uyaAuthoredClass500} class-500; " +
            $"{_uyaAdmittedClass5821}/{_uyaAuthoredClass5821} class-5821 profiles admitted; " +
            $"{gameplayContext.Gameplay.TargetGroups.Count} target groups / " +
            $"{gameplayContext.Gameplay.TargetPolygons.Count} polygons / " +
            $"{gameplayContext.Gameplay.TargetVolumes.Count} volumes sidecar";
        GD.Print(
            $"[uya-gameplay] {_uyaGameplayStatus}; {_uyaClass500Nodes.Count} class-500 presented, " +
            $"{unpresented} unpresented, {rejected} class-500 rejected, " +
            $"{rejected5821} class-5821 rejected");
    }

    private void ArmUyaGameplay(PlayerHost player)
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

    private void TickUyaGameplay(double delta)
    {
        if (_world?.Game != "rac3" ||
            _player is null ||
            _uyaGameplayContext is null ||
            _uyaClass5821AuditSources.Count == 0)
        {
            return;
        }

        _uyaTargetAuditAge += delta;
        UyaNativePoint ratchet = new(
            -_player.GlobalPosition.X,
            _player.GlobalPosition.Z,
            _player.GlobalPosition.Y);

        int primarySeeded = 0;
        int secondarySeeded = 0;
        int primaryUnknown = 0;
        int secondaryUnknown = 0;

        foreach (UyaClass5821AuditSource audit in _uyaClass5821AuditSources)
        {
            if (!TryAuditUyaRatchetSeed(
                    audit,
                    ratchet,
                    UyaClass5821Actor.NativeObservedSelectorSubtypePrimary,
                    out bool primarySelected))
            {
                primaryUnknown++;
            }
            else if (primarySelected)
            {
                primarySeeded++;
            }

            if (!TryAuditUyaRatchetSeed(
                    audit,
                    ratchet,
                    UyaClass5821Actor.NativeObservedSelectorSubtypeSecondaryA,
                    out bool secondarySelected))
            {
                secondaryUnknown++;
            }
            else if (secondarySelected)
            {
                secondarySeeded++;
            }
        }

        _uyaPrimaryRatchetSeeded = primarySeeded;
        _uyaSecondaryRatchetSeeded = secondarySeeded;
        _uyaPrimaryTargetAuditUnknown = primaryUnknown;
        _uyaSecondaryTargetAuditUnknown = secondaryUnknown;

        if (!_uyaTargetAuditLogged && _uyaTargetAuditAge >= 0.75d)
        {
            _uyaTargetAuditLogged = true;
            GD.Print(
                $"[uya-gameplay] class-5821 Ratchet-seed audit: " +
                $"primary={primarySeeded}/{_uyaClass5821AuditSources.Count}, " +
                $"secondary={secondarySeeded}/{_uyaClass5821AuditSources.Count}, " +
                $"unknown={primaryUnknown}/{secondaryUnknown}; live subtype +0x95 not host-owned");
        }
    }

    private bool TryAuditUyaRatchetSeed(
        UyaClass5821AuditSource audit,
        UyaNativePoint ratchet,
        byte nativeSubtype,
        out bool selected)
    {
        selected = false;
        if (_uyaGameplayContext is null)
            return false;

        UyaClass5821AuthoredState authored = audit.Authored;
        UyaClass5821TargetSelectionRequest request;
        try
        {
            request = UyaClass5821Actor.BuildTable1TargetSelectionRequest(
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
        UyaMobyPlacementFacts placement = audit.Placement;
        if (request.NativeSelectorIndex >= 0)
        {
            if (!_uyaGameplayContext.Gameplay.TryContainsTargetGroup(
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
            UyaClass5821RadiusHeightFacts facts =
                UyaClass5821TargetSelector.BuildNativeRadiusHeightFacts(
                    placement.NativeX,
                    placement.NativeY,
                    placement.NativeZ,
                    ratchet.X,
                    ratchet.Y,
                    ratchet.Z);
            eligible =
                UyaClass5821TargetSelector.IsNativeRadiusHeightEligible(
                    facts,
                    request);
        }

        UyaClass5821TargetScoreFacts scoreFacts =
            UyaClass5821TargetSelector.BuildNativeScoreFacts(
                placement.NativeX,
                placement.NativeY,
                placement.RotationZ,
                ratchet.X,
                ratchet.Y,
                isRatchet: true);
        var ratchetFacts = new UyaClass5821TargetCandidateFacts(
            UyaGameplayEntityRef.Player,
            IsRatchet: true,
            NativeRegistryTag: null,
            IsEligible: eligible,
            scoreFacts.HorizontalDistance,
            scoreFacts.ShortestHeadingErrorRadians);

        UyaClass5821TargetSelectionResult? result =
            UyaClass5821TargetSelector.SelectOrdinaryTarget(
                request,
                ratchetFacts,
                registryCandidates: []);
        selected = result is { IsRatchet: true };
        return true;
    }

    private sealed record UyaClass5821AuditSource(
        RuntimeDynamicObject Source,
        UyaClass5821AuthoredState Authored,
        UyaMobyPlacementFacts Placement);

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
            $"presented live {live}, destroyed {_uyaDestroyedClass500}\n" +
            $"class-5821 profiles {_uyaAdmittedClass5821}/{_uyaAuthoredClass5821}; " +
            $"Ratchet seed audit primary {_uyaPrimaryRatchetSeeded}, secondary {_uyaSecondaryRatchetSeeded}, " +
            $"unknown {_uyaPrimaryTargetAuditUnknown}/{_uyaSecondaryTargetAuditUnknown}\n" +
            $"live subtype +0x95 / runtime AI state / player consequence pending   {_uyaGameplayStatus}";
    }
}
