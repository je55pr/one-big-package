using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.IO;
using OBP.RAC1.Level;
using OBP.RAC1.Progression;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>
/// Source-authored mission actor, scene and prompt lifetime for one R&C1 world.
/// The persistent campaign and currently selected wallet are borrowed through
/// explicit ports; this world lease does not own or restore a player profile.
/// </summary>
internal sealed class Rac1MissionWorldHost(
    RuntimeWorld world, RuntimeWorldScene.Result scene, Rac1WorldContext context,
    Rac1MissionWorldPorts ports) : IDisposable
{
    private Rac1MissionSession? _session;
    private readonly Dictionary<int, RuntimeWorldScene.DynamicObjectNode> _nodes = [];
    private Rac1MissionSceneUi? _sceneUi;
    private Label? _prompt;
    private bool _ownsPause;
    private bool _interactPressed;
    private bool _exitQueued;
    private double _ticks;
    private int _generation;
    private bool _disposed;

    internal IReadOnlyList<Rac1MissionActorProbe> Probe() =>
        !_disposed && ports.WorldStillCurrent() && _session is { } session ? session.Probe() : [];

    internal void Reset()
    {
        if (_disposed) return;
        ++_generation; // Any already-deferred scene/exit belongs to the prior binding.
        if (_sceneUi is { } currentScene)
        {
            currentScene.Completed -= CompleteScene;
            if (GodotObject.IsInstanceValid(currentScene)) currentScene.QueueFree();
        }
        if (_prompt is { } prompt && GodotObject.IsInstanceValid(prompt)) prompt.QueueFree();
        _sceneUi = null;
        _prompt = null;
        if (_ownsPause) ports.Pause(false);
        _ownsPause = false;
        _interactPressed = _exitQueued = false;
        _session = null;
        _nodes.Clear();
        _ticks = 0;
    }

    internal void Configure()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // A debug/provider load is not permission to progress an unrelated
        // campaign. Preserve the original current-level admission rule.
        var campaign = ports.Campaign();
        if (world.LevelId != campaign.Session.Campaign.CurrentLevel) return;
        string path = ports.SourcePath() ??
            throw new InvalidOperationException("Mission actors require the retail source.");
        using var reader = new FileRandomAccessReader(path);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == world.LevelId);
        var gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
        var instances = Rac1Instances.Parse(gameplay).MobyInstances;
        var overlay = Rac1ExecutableOverlay.Read(reader, level);
        _session = new(world.LevelId, campaign.Session.Campaign, ports.Wallet(), Rac1ClipVolumes.Read(gameplay));
        foreach (var placement in instances)
        {
            if (placement.OClass != 750 && !(world.LevelId == 0 && placement.OClass == 834 || world.LevelId == 1 && placement.OClass == 774))
                continue;
            var authored = (world.DynamicObjects ?? []).SingleOrDefault(s => s.InstanceIndex == placement.Index && s.NativeClassId == placement.OClass);
            if (authored is null) continue;
            if (placement.OClass == 750) _session.RegisterInfobot(placement);
            else
            {
                var program = Rac1TalkProgram.Read(overlay, world.LevelId, placement)
                    ?? throw new InvalidDataException("Mission actor lacks its native talk program.");
                _session.RegisterTalkActor(placement, program, postOpeningIntro: world.LevelId == 0);
            }
            var node = Rac1WorldPresentation.FindPresentedDynamic(scene, authored)
                ?? Rac1WorldPresentation.CreateInvisibleRuntimeNode(scene, authored);
            context.MobyRuntime.Register(authored, node.State, 0, placement.PVar ?? []);
            _nodes.Add(placement.Index, node);
        }
        GD.Print($"[rac1-missions] L{world.LevelId}: {_session.RegisteredCount} source-owned mission actors");
    }

    private Rac1MissionPlayerFacts PlayerFacts(PlayerHost player, bool interact = false) => new(
        Rac1LootPresentation.ToEffectNative(player.GlobalPosition), player.Rac1CurrentYaw,
        ports.Health().Probe().Nanotech, InteractPressed: interact,
        GameplayInhibited: !player.Rac1GameplayAllowsOrdinaryMovement || player.DevelopmentControls.FlyEnabled ||
            player.Rac1RemoteControlActive || ports.QuickSelectOpen());

    internal bool TryHandleInteraction(InputEvent input)
    {
        if (_disposed || !ports.WorldStillCurrent() || _session is null ||
            ports.Player() is not { } player || ports.Paused() ||
            !input.IsActionPressed(ControllerBindings.QuickSelect) ||
            !_session.CanInteract(PlayerFacts(player))) return false;
        _interactPressed = true;
        return true;
    }

    internal void Tick(double delta)
    {
        if (_disposed || !ports.WorldStillCurrent() || _session is null ||
            ports.Player() is not { } player || _exitQueued) return;
        _ticks += Math.Max(0, delta) * OBP.RAC1.Player.Rac1RatchetMovementController.UpdateHz;
        while (_ticks >= 1)
        {
            _ticks--;
            var tick = _session.Step(PlayerFacts(player, _interactPressed));
            _interactPressed = false;
            if (tick.WalletChanged) ports.RefreshHud();
            foreach (var admission in tick.Admissions)
            {
                ports.Persist($"native class-{admission.Owner.NativeClassId} i{admission.Owner.InstanceIndex} state {admission.NativeState} admitted {admission.Destination}");
                GD.Print($"[rac1-missions] DISCOVERY owner={admission.Owner} destination={admission.Destination}");
            }
            foreach (var probe in _session.Probe())
            {
                if (!_nodes.TryGetValue(probe.Key.InstanceIndex, out var node)) continue;
                var instance = context.MobyRuntime.Require(node.Source);
                context.MobyRuntime.SetNativeState(instance, probe.NativeState);
                context.MobyRuntime.SetPresence(instance, probe.Visible ? RuntimeEntityPresence.Active : RuntimeEntityPresence.Inactive);
                node.ApplyState(instance.EntityState);
            }
            if (_session.PendingScene is { } request)
            {
                int generation = _generation;
                Callable.From(() => PresentScene(request, generation)).CallDeferred();
                break;
            }
            if (tick.LevelExit is { } exit)
            {
                _exitQueued = true;
                int generation = _generation;
                Callable.From(() => ApplyLevelExit(exit, generation)).CallDeferred();
                break;
            }
        }
        if (_prompt is null)
        {
            _prompt = new Label { Name = "MissionInteraction", Position = new(26, 430), Text = "Triangle / Q: Talk" };
            ports.AttachPrompt(_prompt);
        }
        _prompt.Visible = _session.CanInteract(PlayerFacts(player));
    }

    private void PresentScene(Rac1MissionSceneRequest request, int generation)
    {
        if (_disposed || generation != _generation || !ports.WorldStillCurrent() ||
            _session?.PendingScene is not { } pending || !ReferenceEquals(pending, request)) return;
        if (_sceneUi is null)
        {
            _sceneUi = new Rac1MissionSceneUi();
            _sceneUi.Completed += CompleteScene;
            ports.AttachScene(_sceneUi);
        }
        _ownsPause = true;
        ports.Pause(true);
        _sceneUi.Present(request);
        GD.Print($"[rac1-missions] scene owner={request.Owner} {request.Kind}={request.NativeSceneId}");
    }

    private void CompleteScene(Rac1MissionSceneRequest request)
    {
        if (_disposed || !ports.WorldStillCurrent() || _session?.CompleteScene(request) != true) return;
        if (_session.PendingScene is { } next)
        {
            _sceneUi!.Present(next);
            return;
        }
        _sceneUi!.Completed -= CompleteScene;
        _sceneUi.QueueFree();
        _sceneUi = null;
        if (_ownsPause) ports.Pause(false);
        _ownsPause = false;
        // The acknowledgment button must not become a gameplay jump on return.
        if (ports.Player() is { } player)
            player.SuppressButtonsUntilRelease(player.MenuHeldButtons);
    }

    private void ApplyLevelExit(Rac1MissionLevelExit exit, int generation)
    {
        // Reset already clears the exit flag and advances the generation. Never
        // let an old callback change the state of a newly configured lease.
        if (_disposed || generation != _generation) return;
        bool started = false;
        try
        {
            if (!ports.WorldStillCurrent() || world.LevelId != exit.Owner.LevelId) return;
            var destination = TrilogyWorldProviders.Resolve($"rac1:LEVEL{exit.Destination}")
                ?? throw new InvalidOperationException("Native mission requested an unavailable source destination.");
            started = ports.Campaign().Session.BeginMissionLevelExit(exit) == Rac1PlanetTravelStartResult.Started;
            if (!started) return;

            GD.Print($"[rac1-missions] native actor exit L{world.LevelId} -> {exit.Destination}");
            ports.Navigate(destination);
        }
        finally
        {
            // A rejected or stale exit must not disable mission ticks for the
            // rest of an otherwise live world. Once native travel is accepted,
            // retain the guard to prevent a second level-exit dispatch.
            if (!started && !_disposed && generation == _generation) _exitQueued = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Reset();
        _disposed = true;
    }
}
