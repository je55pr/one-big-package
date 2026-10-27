using System.Collections.Immutable;
using System.Numerics;
using OBP.Core;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime.Gameplay;

namespace OBP.Game.Diagnostics;

public abstract class Rac1CombatScenarioSupport(MovementDiagnosticContext context, Rac1CombatScenarioPorts ports)
    : Rac1EncounterScenarioSupport(context, ports.Enemies, ports.Actors)
{
    protected Rac1CombatScenarioPorts Ports { get; } = ports;
    protected IRac1CombatDiagnosticFixture Combat => Ports.Combat;
    protected IRac1EncounterLifecycle lifecycle => Ports.Lifecycle;
    protected ImmutableDictionary<int, Rac1DiagnosticObject> Hostiles => Ports.Combat.ReadHostiles().ToImmutableDictionary(key => key.Target.InstanceIndex, key => new Rac1DiagnosticObject(key, Ports.Combat));
    protected ImmutableArray<Rac1DiagnosticObject> Crates => [.. Ports.Combat.ReadCrates().Select(key => new Rac1DiagnosticObject(key, Ports.Combat))];
    protected bool TryGetRac1Hostile(int id, out Rac1DiagnosticObject? hostile, out Rac1ToadDiagnosticSnapshot? probe)
    { hostile = Hostiles.GetValueOrDefault(id); probe = hostile is null ? null : Enemies.ReadToad(hostile.Key); return hostile is not null; }
    protected int CountRac1AuthoredClass749() => Combat.ReadPopulation().AuthoredClass749;
    protected int CountRac1PresentedClass749() => Combat.ReadPopulation().PresentedClass749;
    protected void AssertRac1SmokeHudWeapon(string key, int? expectedAmmo) => Rac1CombatAssertions.HudWeapon(Combat.ReadPresentation().Weapon, key, expectedAmmo);
    protected void AssertRac1SmokeWrenchPresentation(bool visible, string phase) => Rac1CombatAssertions.Wrench(Combat.ReadPresentation(), visible, phase);
    protected void Rac1SmokePlaceGroundedFacing(Vector3 position, Vector3 direction) => Combat.PlacePlayerFacing(position, direction, true);
    protected void Rac1SmokePlaceFacing(Vector3 position, Vector3 direction) => Combat.PlacePlayerFacing(position, direction, false);
    protected Task<float> Rac1SmokeApproachWrenchTargetAsync(Func<Vector3> targetCenterProvider, int maxFrames, string label, float inputScale = 1, float minimumTravel = .5f, float maximumPlanarDistance = float.PositiveInfinity) =>
        new Rac1WrenchApproachScenario(Context, Ports.Approach, () => string.Join(';', Combat.ReadPlayerContactDescriptions())).RunAsync(targetCenterProvider, maxFrames, label, inputScale, minimumTravel, maximumPlanarDistance);

    protected async Task<(
        float PlayerTravel,
        float HostileTravel,
        double EntryDistance,
        double EntryFacing,
        Rac1Class749AttackEvent Attack,
        int AttackerInstance)> Rac1SmokeProvokeClass749AttackAsync(
        Rac1DiagnosticObject hostile,
        int maxFrames)
    {
        if (World.Read() is not { Source: ObpSourceGame.Rac1, LevelId: Rac1Class749VeldinPopulation.LevelId } ||
            !hostile.Read().RecoveredVeldinPlacement)
            throw new InvalidOperationException(
                "Natural class-749 attack smoke requires a recovered Veldin population placement.");

        var nanotechBefore = lifecycle.ReadNanotech();
        if (nanotechBefore.IsDead || nanotechBefore.Nanotech != 4)
            throw new InvalidOperationException(
                $"Natural class-749 attack smoke requires 4 live Nanotech, found {nanotechBefore.Nanotech}.");

        Vector3 playerStart = Player.Read().GlobalPosition;
        var groupStarts = Hostiles.ToDictionary(pair => pair.Key, pair => pair.Value.Read().Position);
        var groupEntries = new Dictionary<int, (double Distance, double Facing)>();
        double? entryDistance = null;
        double? entryFacing = null;
        var pursuedMembers = new HashSet<int>();
        int activationGroup = Rac1Class749VeldinPopulation.GetActivationGroup(
            hostile.Key.Target.InstanceIndex);
        int expectedGroupMembers = Hostiles.Keys.Count(instanceIndex =>
            Rac1Class749VeldinPopulation.TryGetActivationGroup(instanceIndex, out int group) &&
            group == activationGroup);
        var activatedGroupMembers = new HashSet<int>();
        Rac1GameplayDamageDispatch? observedAttackDispatch = null;
        void ObserveDamage(Rac1GameplayDamageDispatch dispatch)
        {
            if (observedAttackDispatch is null &&
                dispatch.Damage.Source.NativeClassId == 749 &&
                Rac1Class749VeldinPopulation.TryGetActivationGroup((int)dispatch.Damage.Source.RuntimeId, out int sourceGroup) &&
                sourceGroup == activationGroup &&
                dispatch.Damage.Target == Rac1GameplayEntityRef.Player)
            {
                observedAttackDispatch = dispatch;
            }
        }

        var damage = Driver.Own(Combat.SubscribeDamage(ObserveDamage));
        try
        {
            for (int frame = 0; frame < maxFrames; frame++)
            {
                if (!Combat.IsAvailable(hostile.Key) || !hostile.Read().Visible)
                    throw new InvalidOperationException(
                        $"Recovered class-749 i{hostile.Key.Target.InstanceIndex} disappeared during ordinary-play approach: " +
                        $"state={Enemies.ReadToad(hostile.Key).NativeState}, health={Enemies.ReadToad(hostile.Key).Health}.");

                var probe = Enemies.ReadToad(hostile.Key);
                Vector3 toPlayer = Player.Read().GlobalPosition - hostile.Read().Position;
                double distance = toPlayer.Length();
                double facing = Rac1SmokeClass749FacingError(hostile, Player.Read().GlobalPosition);

                foreach (var pair in Hostiles)
                {
                    var memberProbe = Enemies.ReadToad(pair.Value.Key);
                    if (Rac1Class749VeldinPopulation.TryGetActivationGroup(pair.Key, out int group) &&
                        group == activationGroup &&
                        memberProbe.NativeState != Rac1Class749Hostile.TargetSearchNativeState)
                    {
                        activatedGroupMembers.Add(pair.Key);
                        if (memberProbe.NativeState == Rac1Class749Hostile.TargetedNativeState)
                            pursuedMembers.Add(pair.Key);
                        if (memberProbe.NativeState == Rac1Class749Hostile.AttackNativeState && !groupEntries.ContainsKey(pair.Key))
                        {
                            groupEntries.Add(pair.Key, (pair.Value.Read().Position.DistanceTo(Player.Read().GlobalPosition),
                                Rac1SmokeClass749FacingError(pair.Value, Player.Read().GlobalPosition)));
                        }
                    }
                }

                if (probe.NativeState == Rac1Class749Hostile.AttackNativeState &&
                    entryDistance is null)
                {
                    entryDistance = distance;
                    entryFacing = facing;
                    if (!(distance < Rac1Class749Hostile.AttackDistanceExclusive) ||
                        !(facing < Rac1Class749Hostile.AttackFacingErrorExclusive))
                        throw new InvalidOperationException(
                            $"Class-749 entered attack outside recovered gates: distance={distance:R}, facing={facing:R}.");
                }

                if (observedAttackDispatch is { } dispatch)
                {
                    var damageEvent = dispatch.Damage;
                    int attacker = (int)damageEvent.Source.RuntimeId;
                    Require(groupEntries.TryGetValue(attacker, out var attackEntry), "attacker never entered its native attack state");
                    entryDistance = attackEntry.Distance; entryFacing = attackEntry.Facing;
                    var nanotechAfter = lifecycle.ReadNanotech();
                    // Validate the member that actually produced admitted player
                    // damage. Other members may already be mid-attack from an
                    // earlier fixture phase when this observation begins.
                    if (!pursuedMembers.Contains(attacker) ||
                        !(attackEntry.Distance < Rac1Class749Hostile.AttackDistanceExclusive) ||
                        !(attackEntry.Facing < Rac1Class749Hostile.AttackFacingErrorExclusive))
                        throw new InvalidOperationException(
                            "Class-749 reached its transported attack marker without observed pursuit/attack entry.");
#pragma warning disable S1244 // Transport must retain the exact recovered attack marker and damage constants.
                    if (damageEvent.NativeMarker is not double nativeMarker ||
                        nativeMarker != Rac1Class749Hostile.AttackMarker ||
                        damageEvent.NativeDamage != Rac1Class749Hostile.AttackDamage)
                    {
                        throw new InvalidOperationException(
                            $"Class-749 transported marker/damage {damageEvent.NativeMarker?.ToString("R") ?? "null"}/{damageEvent.NativeDamage:R}, " +
                            $"expected {Rac1Class749Hostile.AttackMarker:R}/{Rac1Class749Hostile.AttackDamage:R}.");
                    }
#pragma warning restore S1244
                    if (nanotechAfter.Nanotech != nanotechBefore.Nanotech - 1 || nanotechAfter.IsDead)
                        throw new InvalidOperationException(
                            $"Class-749 ordinary attack changed Nanotech {nanotechBefore.Nanotech}->{nanotechAfter.Nanotech}, expected exactly one.");

                    Vector3 playerTravel = Player.Read().GlobalPosition - playerStart;
                    playerTravel.Y = 0f;
                    Vector3 hostileTravel = Hostiles[attacker].Read().Position - groupStarts[attacker];
                    hostileTravel.Y = 0f;
                    if (playerTravel.Length() < 0.5f)
                        throw new InvalidOperationException(
                            "Natural class-749 attack did not require meaningful ordinary Ratchet movement.");
                    if (hostileTravel.Length() < 0.25f)
                        throw new InvalidOperationException(
                            "Natural class-749 attack did not visibly exercise hostile pursuit.");
                    if (expectedGroupMembers > 1 &&
                        activatedGroupMembers.Count < expectedGroupMembers)
                    {
                        throw new InvalidOperationException(
                            $"Class-749 activation group {activationGroup} activated only " +
                            $"{activatedGroupMembers.Count}/{expectedGroupMembers} authored placements: " +
                            $"{string.Join(",", activatedGroupMembers.Order())}.");
                    }

                    Log(
                        $"[rac1-smoke] class-749 group {activationGroup} activation PASS; " +
                        $"members={string.Join(",", activatedGroupMembers.Order())}");

                    return (
                        playerTravel.Length(),
                        hostileTravel.Length(),
                        entryDistance.Value,
                        entryFacing.Value,
                        new Rac1Class749AttackEvent(
                            nativeMarker,
                            damageEvent.NativeDamage), attacker);
                }

                if (lifecycle.ReadNanotech().Nanotech != nanotechBefore.Nanotech)
                    throw new InvalidOperationException(
                        "Nanotech changed before the smoke observed the recovered class-749 attack marker.");

                if (probe.NativeState == Rac1Class749Hostile.AttackNativeState)
                {
                    ClearMovementSmokeInput();
                }
                else
                {
                    // Settle at the attack's reach rather than driving through its
                    // contact sphere into the enemy body during the wind-up.
                    Rac1SmokeDriveToward(hostile.Read().Position, distance < 4 ? .35f : 1f);
                }

                await ProcessFrameAsync();
            }
        }
        finally
        {
            CleanupStep(damage.Dispose);
            ClearMovementSmokeInput();
        }

        var finalProbe = Hostiles.ContainsKey(hostile.Key.Target.InstanceIndex)
            ? Enemies.ReadToad(hostile.Key)
            : null;
        throw new TimeoutException(
            $"Timed out provoking retained class-749 through ordinary play: " +
            $"player={Player.Read().GlobalPosition}, hostile={hostile.Read().Position}, " +
            $"state={finalProbe?.NativeState}, Nanotech={lifecycle.ReadNanotech().Nanotech}.");
    }


    private static double Rac1SmokeClass749FacingError(
        Rac1DiagnosticObject hostile,
        Vector3 target)
    {
        Vector3 toTarget = target - hostile.Read().Position;
        toTarget.Y = 0f;
        Vector3 forward = hostile.Read().Forward;
        forward.Y = 0f;
        if (toTarget.LengthSquared() <= 1e-6f || forward.LengthSquared() <= 1e-6f)
            return Math.PI;

        return Math.Abs(
            MathF.Atan2(Vector3.Cross(forward.Normalized(), toTarget.Normalized()).Length(), Vector3.Dot(forward.Normalized(), toTarget.Normalized())));
    }


    protected void Rac1SmokeDriveToward(Vector3 target, float inputScale = 1f)
    {
        if (!(inputScale > 0f && inputScale <= 1f))
            throw new ArgumentOutOfRangeException(nameof(inputScale));

        Vector3 desired = target - Player.Read().GlobalPosition;
        desired.Y = 0f;
        if (desired.LengthSquared() <= 1e-5f)
        {
            ClearMovementSmokeInput();
            return;
        }
        desired = desired.Normalized();

        var axes = Rac1EncounterInput.ConditionedAxesToward(desired, Ports.Approach.ControlYaw, inputScale);
        SetRac1SmokeConditionedInput(axes.Right, axes.Forward);
    }

}
