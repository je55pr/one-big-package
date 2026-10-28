using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.IO;
using OBP.RAC1.Level;
using OBP.RAC1.Progression;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

public partial class OBPGame
{
    private Rac1MissionSession? _rac1Missions;
    private readonly Dictionary<int, RuntimeWorldScene.DynamicObjectNode> _rac1MissionNodes = [];
    private Rac1MissionSceneUi? _rac1MissionSceneUi;
    private bool _rac1MissionOwnsPause;
    private bool _rac1MissionInteractPressed;
    private bool _rac1MissionExitQueued;
    private double _rac1MissionTicks;
    private Label? _rac1MissionPrompt;

    private void ResetRac1Missions()
    {
        if (_rac1MissionSceneUi is { } scene && IsInstanceValid(scene)) scene.QueueFree();
        if (_rac1MissionPrompt is { } prompt && IsInstanceValid(prompt)) prompt.QueueFree();
        _rac1MissionSceneUi = null;
        _rac1MissionPrompt = null;
        if (_rac1MissionOwnsPause) GetTree().Paused = false;
        _rac1MissionOwnsPause = false;
        _rac1MissionInteractPressed = _rac1MissionExitQueued = false;
        _rac1Missions = null;
        _rac1MissionNodes.Clear();
        _rac1MissionTicks = 0;
    }

    private void ConfigureRac1Missions(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        // An external provider/debug load is not permission to mutate a different
        // campaign. Ordinary current-level entry and actor/map travel are eligible.
        if (world.LevelId != _rac1Campaign.Session.Campaign.CurrentLevel) return;
        var source = Sources.Setup.Library.Get(ObpSourceGame.Rac1) ?? throw new InvalidOperationException("Mission actors require the retail source.");
        using var reader = new FileRandomAccessReader(source.Path);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == world.LevelId);
        var gameplay = Rac1LevelSettings.ReadGameplay(reader, level);
        var instances = Rac1Instances.Parse(gameplay).MobyInstances;
        var overlay = Rac1ExecutableOverlay.Read(reader, level);
        _rac1Missions = new(world.LevelId, _rac1Campaign.Session.Campaign, _rac1BoltWallet, Rac1ClipVolumes.Read(gameplay));
        foreach (var placement in instances)
        {
            if (placement.OClass != 750 && !(world.LevelId == 0 && placement.OClass == 834 || world.LevelId == 1 && placement.OClass == 774))
                continue;
            var authored = (world.DynamicObjects ?? []).SingleOrDefault(s => s.InstanceIndex == placement.Index && s.NativeClassId == placement.OClass);
            if (authored is null) continue;
            if (placement.OClass == 750) _rac1Missions.RegisterInfobot(placement);
            else
            {
                var program = Rac1TalkProgram.Read(overlay, world.LevelId, placement)
                    ?? throw new InvalidDataException("Mission actor lacks its native talk program.");
                _rac1Missions.RegisterTalkActor(placement, program, postOpeningIntro: world.LevelId == 0);
            }
            var node = Rac1WorldPresentation.FindPresentedDynamic(result, authored) ?? Rac1WorldPresentation.CreateInvisibleRuntimeNode(result, authored);
            Rac1World.Context.MobyRuntime.Register(authored, node.State, 0, placement.PVar ?? []);
            _rac1MissionNodes.Add(placement.Index, node);
        }
        GD.Print($"[rac1-missions] L{world.LevelId}: {_rac1Missions.RegisteredCount} source-owned mission actors");
    }

    private Rac1MissionPlayerFacts Rac1MissionPlayer(bool interact = false) => new(
        ToEffectNative(_player!.GlobalPosition), _player.Rac1CurrentYaw, _rac1Nanotech.Probe().Nanotech,
        InteractPressed: interact,
        GameplayInhibited: !_player.Rac1GameplayAllowsOrdinaryMovement || _player.DevelopmentControls.FlyEnabled ||
            _player.Rac1RemoteControlActive || QuickSelectOpen);

    private bool TryHandleRac1MissionInteraction(InputEvent input)
    {
        if (_rac1Missions is null || _player is null || GetTree().Paused ||
            !input.IsActionPressed(ControllerBindings.QuickSelect) ||
            !_rac1Missions.CanInteract(Rac1MissionPlayer())) return false;
        _rac1MissionInteractPressed = true;
        return true;
    }

    private void TickRac1Missions(double delta)
    {
        if (_rac1Missions is null || _player is null || _rac1MissionExitQueued) return;
        _rac1MissionTicks += Math.Max(0, delta) * OBP.RAC1.Player.Rac1RatchetMovementController.UpdateHz;
        while (_rac1MissionTicks >= 1)
        {
            _rac1MissionTicks--;
            var tick = _rac1Missions.Step(Rac1MissionPlayer(_rac1MissionInteractPressed));
            _rac1MissionInteractPressed = false;
            if (tick.WalletChanged) RefreshRac1HudState();
            foreach (var admission in tick.Admissions)
            {
                PersistRac1CampaignState($"native class-{admission.Owner.NativeClassId} i{admission.Owner.InstanceIndex} state {admission.NativeState} admitted {admission.Destination}");
                GD.Print($"[rac1-missions] DISCOVERY owner={admission.Owner} destination={admission.Destination}");
            }
            foreach (var probe in _rac1Missions.Probe())
            {
                if (!_rac1MissionNodes.TryGetValue(probe.Key.InstanceIndex, out var node)) continue;
                var instance = Rac1World.Context.MobyRuntime.Require(node.Source);
                Rac1World.Context.MobyRuntime.SetNativeState(instance, probe.NativeState);
                Rac1World.Context.MobyRuntime.SetPresence(instance, probe.Visible ? RuntimeEntityPresence.Active : RuntimeEntityPresence.Inactive);
                node.ApplyState(instance.EntityState);
            }
            if (_rac1Missions.PendingScene is { } scene)
            {
                Callable.From(() => PresentRac1MissionScene(scene)).CallDeferred();
                break;
            }
            if (tick.LevelExit is { } exit)
            {
                _rac1MissionExitQueued = true;
                Callable.From(() => ApplyRac1MissionLevelExit(exit)).CallDeferred();
                break;
            }
        }
        if (_rac1MissionPrompt is null)
        {
            _rac1MissionPrompt = new Label { Name = "MissionInteraction", Position = new(26, 430), Text = "Triangle / Q: Talk" };
            _ui.AddChild(_rac1MissionPrompt);
        }
        _rac1MissionPrompt.Visible = _rac1Missions.CanInteract(Rac1MissionPlayer());
    }

    private void PresentRac1MissionScene(Rac1MissionSceneRequest request)
    {
        if (_rac1Missions?.PendingScene is not { } pending || !ReferenceEquals(pending, request)) return;
        if (_rac1MissionSceneUi is null)
        {
            _rac1MissionSceneUi = new Rac1MissionSceneUi();
            _rac1MissionSceneUi.Completed += CompleteRac1MissionScene;
            AddChild(_rac1MissionSceneUi);
        }
        _rac1MissionOwnsPause = true;
        GetTree().Paused = true;
        _rac1MissionSceneUi.Present(request);
        GD.Print($"[rac1-missions] scene owner={request.Owner} {request.Kind}={request.NativeSceneId}");
    }

    private void CompleteRac1MissionScene(Rac1MissionSceneRequest request)
    {
        if (_rac1Missions?.CompleteScene(request) != true) return;
        if (_rac1Missions.PendingScene is { } next)
        {
            _rac1MissionSceneUi!.Present(next);
            return;
        }
        _rac1MissionSceneUi!.QueueFree();
        _rac1MissionSceneUi = null;
        if (_rac1MissionOwnsPause) GetTree().Paused = false;
        _rac1MissionOwnsPause = false;
        // A press used to acknowledge a scene must not turn into a gameplay jump.
        _player?.SuppressButtonsUntilRelease(_player.MenuHeldButtons);
    }

    private void ApplyRac1MissionLevelExit(Rac1MissionLevelExit exit)
    {
        if (_world is not { Game.SourceGame: ObpSourceGame.Rac1 } world || world.LevelId != exit.Owner.LevelId) return;
        var destination = TrilogyWorldProviders.Resolve($"rac1:LEVEL{exit.Destination}")
            ?? throw new InvalidOperationException("Native mission requested an unavailable source destination.");
        if (_rac1Campaign.Session.BeginMissionLevelExit(exit) != Rac1PlanetTravelStartResult.Started) return;
        GD.Print($"[rac1-missions] native actor exit L{world.LevelId} -> {exit.Destination}");
        OnDestinationChosen(destination);
    }
}
