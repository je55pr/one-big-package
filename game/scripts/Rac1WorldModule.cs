using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>
/// One lease's R&C1 lifecycle adapter. Native controllers own their rules and
/// clocks; the module preserves the host callback order and owns only world transients.
/// </summary>
internal sealed class Rac1WorldModule : IDisposable
{
    private readonly RuntimeWorld _world;
    private readonly RuntimeWorldScene.Result _scene;
    private readonly Rac1WorldResetPorts _reset;
    private readonly Rac1WorldActorPorts _actors;
    private readonly Rac1WorldTickPorts _ticks;
    private readonly Rac1PlayerGameplayPorts _playerPorts;
    private readonly Rac1WorldContactPorts _contacts;
    private readonly Rac1PlayerGameplayBinding _binding;
    private PlayerHost? _player;
    private bool _configured;
    private bool _disposed;

    internal Rac1WorldModule(RuntimeWorld world, RuntimeWorldScene.Result scene, Rac1PickupRandom random,
        Rac1WorldResetPorts reset, Rac1WorldActorPorts actors, Rac1WorldTickPorts ticks,
        Rac1PlayerGameplayPorts player, Rac1WorldContactPorts contacts, Rac1GameplayHudPorts hud)
    {
        _world = world; _scene = scene; _reset = reset; _actors = actors; _ticks = ticks;
        _playerPorts = player; _contacts = contacts; _binding = new(player);
        Context = new(world.LevelId, random);
        Hud = new(world, Context, Presentation, hud, () => _disposed);
    }

    internal Rac1WorldContext Context { get; }
    internal Rac1WorldPresentation Presentation { get; } = new();
    internal Rac1GameplayHudPresenter Hud { get; }

    internal void Configure()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_configured) throw new InvalidOperationException("World gameplay is already configured.");
        _configured = true;
        ResetBindings();
        _playerPorts.ResetParityHealth();
        Context.BombGlove = new(_playerPorts.Weapons());
        _reset.AdditionalWeapons();
        _reset.BombPresentation();
        _playerPorts.ConfigureWeapons(_world);
        if (_playerPorts.HasFusion())
        {
            Context.BombGlove = new(_playerPorts.Weapons());
            _reset.AdditionalWeapons();
        }
        Hud.Begin();
        ConfigureActors(_world, _scene);
        _playerPorts.ConfigurePresentation(_world);
    }

    private void ResetBindings()
    {
        _reset.Player();
        Context.EquipmentUsePressed = false;
        _reset.Wheel();
        _reset.Taxis();
        _reset.Water();
        _reset.Music();
        _reset.RedPlants();
        _reset.PulsingOrbs();
        _reset.Explosives();
        _reset.Checkpoints();
        _reset.Missions();
        _reset.MetalDetector();
        _reset.Wrench();
        _reset.Retry();
        _reset.Enemies();
        _reset.Pickups();
        _reset.GoldBolts();
        _reset.Cranks();
        _reset.Breakables();
        _reset.Mechanisms();
        Presentation.Dispose();
    }

    internal void Arm(PlayerHost player)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _binding.Arm(player);
        _player = player;
        BindSourcePlayerContacts(player);
    }

    private void BindSourcePlayerContacts(PlayerHost player)
    {
        if (_world.Game != ObpSourceGame.Rac1) return;
        player.Rac1CrankInput = _contacts.Crank;
        player.Rac1WaterSurfaceHeight = _contacts.WaterHeight;
        player.Rac1NovalisDrowned += _contacts.Drowned;
        player.Rac1WaterSoundRequested += _contacts.WaterSound;
        player.ConfigureRac1TraversalTargets(_world.DynamicObjects ?? []);
    }

    internal void Tick(double delta)
    {
        if (_disposed || _ticks.Suspended() || _world.Game != ObpSourceGame.Rac1 ||
            _player is null || !GodotObject.IsInstanceValid(_player)) return;
        if (!_playerPorts.Persistent() && !_binding.TickParityLifecycle()) return;
        if (!_playerPorts.Alive()) { _ticks.AbortTaxi(); return; }
        _ticks.Taxis(delta);
        _ticks.Music(delta);
        _ticks.Swing(delta);
        if (!_playerPorts.Ordinary()) _ticks.Hostiles(delta);
        _ticks.RedPlants(delta);
        _ticks.PulsingOrbs(delta);
        _ticks.Explosives(delta);
        _ticks.Checkpoints();
        _ticks.MetalDetector(delta);
        _ticks.Pickups(delta);
        _ticks.GoldBolts();
        _ticks.Missions(delta);
    }

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            if (GodotObject.IsInstanceValid(_player))
            {
                _player!.Rac1NovalisDrowned -= _contacts.Drowned;
                _player.Rac1WaterSoundRequested -= _contacts.WaterSound;
                _player.Rac1CrankInput = null;
                _player.Rac1WaterSurfaceHeight = null;
            }
            _binding.Dispose();
            ResetBindings();
            _reset.AdditionalWeapons();
            _reset.BombPresentation();
        }
        finally
        {
            _player = null;
            Presentation.Dispose();
            Context.BombGlove = null;
            Context.BombFireRequested = false;
            Context.CombatStatus = "off";
            _disposed = true;
        }
    }

    private void ConfigureActors(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        if (world.Game != ObpSourceGame.Rac1)
        {
            return;
        }

        _actors.Pickups(world);
        Context.CombatStatus = "ready";
        foreach (var source in world.DynamicObjects ?? Array.Empty<RuntimeDynamicObject>())
        {
            if (!Rac1BoltCrate.IsCrateClass(source.NativeClassId))
            {
                continue;
            }

            Rac1BoltCrateAuthoredState? authored;
            try
            {
                authored = Rac1BoltCrate.ReadAuthored(source);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (authored is null)
            {
                continue;
            }

            var node = Rac1WorldPresentation.FindPresentedDynamic(result, source)
                ?? Rac1WorldPresentation.CreateInvisibleRuntimeNode(result, source);
            Context.BoltCrates.Register(source, node.State);
            Presentation.CrateNodes.Add(node);
            _actors.CrateCollider(node);
        }

        _actors.Explosives(world, result);
        _actors.Checkpoints(world);
        _actors.Missions(world, result);
        _actors.MetalDetector(world);
        _actors.PlacedBolts(world, result);
        _actors.GoldBolts(world, result);
        _actors.RedPlants(world, result);
        _actors.PulsingOrbs(world, result);
        _actors.Cranks(world, result);
        _actors.Breakables(world, result);
        _actors.Mechanisms(world, result);
        _actors.Water(world);
        _actors.Music(world);

        int authoredClass749 = 0;
        int rejectedHostiles = 0;
        foreach (var hostileSource in (world.DynamicObjects ?? Array.Empty<RuntimeDynamicObject>())
            .Where(source => source.NativeClassId == Rac1Class749Hostile.NativeClassId))
        {
            authoredClass749++;
            var node = Rac1WorldPresentation.FindPresentedDynamic(result, hostileSource)
                ?? Rac1WorldPresentation.CreateInvisibleRuntimeNode(result, hostileSource);
            Presentation.Class749Nodes.Add(node);
            if (!Rac1Class749Hostile.IsRecoveredVeldinPlacement(world.LevelId, hostileSource))
            {
                continue;
            }

            try
            {
                _ = Rac1Class749Hostile.ReadAuthored(hostileSource)
                    ?? throw new InvalidDataException("Class-749 source failed its authored-state contract.");
                var probe = Context.Hostiles.RegisterVeldinPlacement(hostileSource, node.State);
                Presentation.HostileNodes.Add(hostileSource.InstanceIndex, node);
                if (probe.LinkedTargetInstanceIndex is int linkedInstanceIndex)
                    Presentation.TrackLinkedTarget(world, result, linkedInstanceIndex);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
            {
                rejectedHostiles++;
                GD.PrintErr($"[rac1-gameplay] class-749 i{hostileSource.InstanceIndex} unavailable: {ex.Message}");
            }
        }

        _actors.Enemies(world, result);
        _actors.Taxis(world, result);
        GD.Print($"[rac1-gameplay] ready: {Presentation.CrateNodes.Count} admitted retail crates, " +
                 $"{Presentation.HostileNodes.Count} recovered Veldin class-749 placements from " +
                 $"{authoredClass749} authored placements ({rejectedHostiles} rejected)");
    }

}
