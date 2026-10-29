using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using static OBP.Game.Rac1EnemyCoordinates;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

internal sealed partial class Rac1AridiaEncounter : IDisposable
{
    private readonly Rac1EnemyHost _host;
    private bool _disposed;
    internal Rac1AridiaEncounter(Rac1EnemyHost host) => _host = host;
    internal Rac1AridiaEnemySession Session { get => _rac1AridiaEnemies; set => _rac1AridiaEnemies = value; }
    internal int ClipVolumeCount => _rac1AridiaClipVolumes.Count;
    internal System.Numerics.Vector3 ClipVolumeCentre(int index) => _rac1AridiaClipVolumes[index].Centre;
    internal bool ClipVolumeContains(int index, System.Numerics.Vector3 position) => _rac1AridiaClipVolumes[index].Contains(position);
    internal bool TryGetStartup(int id, out Rac1AridiaConstructorbotNative.Frame startup) => _rac1ConstructorbotStartups.TryGetValue(id, out startup);
    internal Vector3? Waypoint(int id) => _rac1ConstructorbotNativeWaypoints.TryGetValue(id, out var point) ? point : null;
    internal Vector3? RoamingTarget(int id) => _rac1ConstructorbotRoamingTargets.TryGetValue(id, out var point) ? point : null;
    internal int? RoamExitFlag(int id) => _rac1ConstructorbotRoamExitFlags.TryGetValue(id, out var flags) ? flags : null;
    internal int NativeLiftCount(int id) => _rac1ConstructorbotNativeLiftCounts.GetValueOrDefault(id);
    internal int SweepProbeCount(int id) => _rac1ConstructorbotSweepProbeCounts.GetValueOrDefault(id);
    internal float VerticalStep(int id) => _rac1ConstructorbotStateThreeVerticalSteps.GetValueOrDefault(id);
    internal void RemoveRoamingTarget(int id) => _rac1ConstructorbotRoamingTargets.Remove(id);
    internal void Reset()
    {
        if (_disposed) return;
        _rac1AridiaEnemies = new();
        ResetRac1AridiaScriptActors();
        ResetRac1ConstructorbotStartups();
    }
    public void Dispose()
    {
        if (_disposed) return;
        Reset();
        _disposed = true;
    }

    private Rac1AridiaEnemySession _rac1AridiaEnemies = new();
    private readonly Dictionary<int, Rac1AridiaConstructorbotNative.Frame> _rac1ConstructorbotStartups = [];
    private readonly Dictionary<int, float> _rac1ConstructorbotWalkerSpeeds = [];
    private readonly Dictionary<int, float> _rac1ConstructorbotWalkerYawVelocities = [];
    private readonly Dictionary<int, int> _rac1ConstructorbotSweepProbeCounts = [];
    private readonly Dictionary<int, int> _rac1ConstructorbotSphereProbeCounts = [];
    private readonly Dictionary<int, int> _rac1ConstructorbotSphereContactCounts = [];
    private readonly Dictionary<int, int> _rac1ConstructorbotNativeLiftCounts = [];
    private SphereShape3D? _rac1ConstructorbotWalkerSphere;
    private readonly Dictionary<int, float> _rac1ConstructorbotStateThreeVerticalSteps = [];
    // Source table at 0x1600ec is the gameplay +0x60 list already decoded
    // as Rac1ClipVolumes: PVar+0x188 selects a record, +0x30 its position.
    private readonly Dictionary<int, Vector3> _rac1ConstructorbotNativeWaypoints = [];
    private readonly Dictionary<int, Vector3> _rac1ConstructorbotRoamingTargets = [];
    // Captures the source walker bitmask only when state6 exits to idle.
    // This lets the retail encounter distinguish arrival (bit4) from a
    // confirmed obstruction (bit2) without reconstructing past contacts.
    private readonly Dictionary<int, int> _rac1ConstructorbotRoamExitFlags = [];
    private IReadOnlyList<Rac1ClipVolume> _rac1AridiaClipVolumes = [];

    private void ResetRac1ConstructorbotStartups()
    {
        _rac1ConstructorbotStartups.Clear();
        _rac1ConstructorbotWalkerSpeeds.Clear();
        _rac1ConstructorbotWalkerYawVelocities.Clear();
        _rac1ConstructorbotSweepProbeCounts.Clear();
        _rac1ConstructorbotSphereProbeCounts.Clear();
        _rac1ConstructorbotSphereContactCounts.Clear();
        _rac1ConstructorbotNativeLiftCounts.Clear();
        _rac1ConstructorbotStateThreeVerticalSteps.Clear();
        _rac1ConstructorbotNativeWaypoints.Clear();
        _rac1ConstructorbotRoamingTargets.Clear();
        _rac1ConstructorbotRoamExitFlags.Clear();
        _rac1AridiaClipVolumes = [];
        _rac1ConstructorbotWalkerSphere?.Dispose();
        _rac1ConstructorbotWalkerSphere = null;
    }

    private int? ResolveAridiaSharkLinkedMobyState(int instanceIndex)
    {
        int? link = _rac1AridiaEnemies.LinkedActivationMobyIndex(instanceIndex);
        if (!link.HasValue) return null;
        // Native class 1818 is the Aridia Sandmouse. Unless its native
        // controller is actually running, the required state >= 5 cannot be
        // inferred from elapsed time or the player's nearby position.
        return _host.Context.MobyRuntime.TryGet(new Rac1MobyRuntimeKey(Rac1AridiaEnemies.Sandmouse, link.Value),
            out var moby) && moby is { IsActive: true }
            ? moby.State.NativeState : null;
    }

    internal void Hide(Rac1HostedEnemy enemy) =>
        enemy.Node.Root.Visible = false;

    internal void Show(Rac1HostedEnemy enemy) =>
        enemy.Node.Root.Visible = _host.IsRac1GameplayActive(enemy.Node) && !enemy.Buried;

    private void ApplyRac1AridiaPlayerDamage(RuntimeWorldScene.DynamicObjectNode source)
    {
        if (_host.Health.Probe().IsDead) return;
        // The original LEVEL2 outgoing damage descriptor is not recovered.
        // Keep this host adapter separate from any claimed native attack flags.
        var damage = new Rac1GameplayDamageEvent(
            new(Rac1GameplayEntityKind.Moby, source.Source.NativeClassId, source.Source.InstanceIndex),
            Rac1GameplayEntityRef.Player, 1);
        var before = _host.Health.Probe();
        _host.Context.DamageTransport.Publish(damage);
        if (_host.PlayerPorts.Persistent())
        {
            _host.PlayerPorts.AridiaSourceDamage(source, _host.NativeFrame, 1, 0);
            return;
        }
        var after = _host.Health.ApplyHostAridiaContactDamage();
        _host.Player!.Rac1GameplayState = after;
        _host.PlayerPorts.Hurt(before.Nanotech, after.Nanotech);
        if (after.Nanotech != before.Nanotech)
            _host.PlayerPorts.Hud([OBP.RAC1.Presentation.Rac1HudProjection.DamageFeedback(before, after)]);
    }

    internal void ActivateNearbyRac1AridiaShark(int nestInstance, Vector3 nestPosition)
    {
        // L2:0x2dd2a8 walks the nest's authored group (+0x21) and selects
        // class 580 in state 26 only. No distance-based candidate fallback.
        int? candidate = _rac1AridiaEnemies.FindWaitingSharkForNest(nestInstance);
        if (candidate is not int id || !_host.Roster.TryGetValue(id, out var shark)) return;
        if (!_rac1AridiaEnemies.ReleaseFromNest(nestInstance, id,
                ToEffectNative(nestPosition), ToEffectNative(shark.AuthoredHomePosition))) return;
        var native = _host.Context.MobyRuntime.Require(shark.Node.Source);
        _host.Context.MobyRuntime.SetNativeState(native, Rac1AridiaEnemies.NestReleasedSharkState);
        var origin = nestPosition + Vector3.Up * Rac1AridiaEnemies.NestReleaseHeight;
        shark.Body.GlobalPosition = origin;
        shark.Node.Root.GlobalPosition = origin;
        shark.NeedsGroundPlacement = false;
        shark.Buried = false;
        shark.DeathTicks = 0;
        shark.Body.CollisionLayer = shark.Body.CollisionMask = 1;
        if (shark.Animation.SupportsSequence(Rac1AridiaEnemies.NestReleasedSharkSequence))
            Rac1EnemyHost.SelectRac1EnemySequence(shark, Rac1AridiaEnemies.NestReleasedSharkSequence, 1);
        GD.Print($"[rac1-aridia] nest released authored class580 instance {id} with native state27/sequence11");
    }

    internal bool ApplyDamage(RuntimeWorldScene.DynamicObjectNode node,
        Rac1GameplayDamageEvent damage)
    {
        if (!_host.OwnsNodeForFamily(node)) return false;
        int id = node.Source.InstanceIndex;
        if (!_rac1AridiaEnemies.Damage(id, damage.NativeDamage)) return false;
        var enemy = _host.Roster[id];
        bool lethal = _rac1AridiaEnemies.Health(id) == 0;
        if (lethal)
        {
            enemy.Body.CollisionLayer = enemy.Body.CollisionMask = 0;
            enemy.DeathTicks = 0;
            int preferredSequence = node.Source.NativeClassId == Rac1AridiaEnemies.Nest ? 3 : 6;
            if (enemy.Animation.SupportsSequence(preferredSequence))
                Rac1EnemyHost.SelectRac1EnemySequence(enemy, preferredSequence, 1);
            if (node.Source.NativeClassId is Rac1AridiaEnemies.Sandshark or Rac1AridiaEnemies.Constructorbot)
            {
                // L2:0x2d54e0 (shark) / 0x2d8430 (bot) call moby_drop_bolts
                // with (moby, 0, -1). Both use the shared authored instance
                // reward word at +0x10, not a fabricated fixed enemy amount.
                int selected = _host.Combat.Reward(Rac1EnemyMotion.RewardCentre(node.Source));
                if (selected > 0)
                    _host.Combat.Pickups(node.Root.GlobalPosition, _host.Context.BoltPickups.Emit(selected));
            }
            // The native nest death path makes an explosion and debris but
            // does not invoke moby_drop_bolts in the translated routine.
        }
        else if (enemy.Animation.SupportsSequence(3))
            Rac1EnemyHost.SelectRac1EnemySequence(enemy, 3, 1);
        GD.Print($"[rac1-aridia] class{node.Source.NativeClassId} i{id}: " +
            (lethal ? "defeated" : $"hit ({_rac1AridiaEnemies.Health(id)} remaining)"));
        return true;
    }

    // Register source-authored actors under the world encounter lease, before
    // the generic host constructs Godot contact and presentation bodies.
    internal void Configure(RuntimeWorld world, RuntimeWorldScene.Result result,
        byte[] levelGameplay, List<RuntimeWorldScene.DynamicObjectNode> nodes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (world.LevelId != Rac1AridiaEnemies.LevelId)
            throw new InvalidOperationException("Aridia encounter attached to another world.");
        Reset();
        var volumes = Rac1ClipVolumes.Read(levelGameplay);
        var authoredPlacements = Rac1Instances.Parse(levelGameplay).MobyInstances;

        _rac1AridiaClipVolumes = volumes;
        _rac1AridiaEnemies.SetAuthoredGroupRoster(Rac1AridiaEnemies.ReadAuthoredGroupRoster(levelGameplay));
        foreach (var authored in (world.DynamicObjects ?? []).Where(item => Rac1AridiaEnemies.IsEnemyClass(item.NativeClassId)))
        {
            var node = Rac1WorldPresentation.FindPresentedDynamic(result, authored) ?? Rac1WorldPresentation.CreateInvisibleRuntimeNode(result, authored);
            byte[] pvar = authoredPlacements[authored.InstanceIndex].PVar
                ?? throw new InvalidDataException("Aridia enemy placement is missing its native PVar.");
            int? linkedActivationMobyIndex = authored.NativeClassId == Rac1AridiaEnemies.Sandshark
                ? Rac1AridiaEnemies.ReadSharkLinkedActivationInstance(pvar) : null;
            // Native class-612 initialization branches by PVar +0x194;
            // the 15 retail bots span four distinct starting states.
            // Other source-gated native states retain existing semantics.
            Rac1AridiaConstructorbotNative.Frame? botStartup =
                authored.NativeClassId == Rac1AridiaEnemies.Constructorbot
                    ? Rac1AridiaConstructorbotNative.Initialize(pvar) : null;
            if (botStartup.HasValue)
            {
                _rac1ConstructorbotStartups.Add(authored.InstanceIndex, botStartup.Value);
                int nativeWaypoint = Rac1AridiaConstructorbotNative.ReadWaypointRecordIndex(
                    pvar, volumes.Count);
                _rac1ConstructorbotNativeWaypoints.Add(authored.InstanceIndex,
                    FromEffectNative(volumes[nativeWaypoint].Centre));
            }
            // The Sandshark init routine's +0x258 branch enters state29.
            var runtime = _host.Context.MobyRuntime.Register(authored, node.State,
                nativeState: botStartup?.NativeState ?? (linkedActivationMobyIndex.HasValue ? 29 : 1),
                pvar: pvar);
            node.ApplyState(runtime.EntityState);
            Vector3 forward = Rac1EnemyHost.EnemyForward(node);
            NVector3 native = ToEffectNative(forward);
            int nestWait = authored.NativeClassId == Rac1AridiaEnemies.Nest
                ? Rac1AridiaEnemies.ReadNestInitialWaitTicks(pvar)
                : 60;
            _rac1AridiaEnemies.Register(authored.InstanceIndex, authored.NativeClassId,
                MathF.Atan2(native.Y, native.X), nestWait,
                linkedActivationMobyIndex: linkedActivationMobyIndex,
                groupId: Rac1AridiaEnemies.ReadAuthoredGroup(authoredPlacements[authored.InstanceIndex].RawRecord),
                constructorbotStartupSequence: botStartup?.Sequence ?? 1);
            nodes.Add(node);
        }
        GD.Print("[rac1-aridia] Constructorbot native startup " +
            string.Join(", ", _rac1ConstructorbotStartups.Values
                .GroupBy(start => start.NativeState).OrderBy(group => group.Key)
                .Select(group => $"state{group.Key}={group.Count()}")));
        ConfigureScriptActors(world, result, authoredPlacements);
    }

}
