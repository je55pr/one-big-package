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

    internal Rac1WorldModule(RuntimeWorld world, RuntimeWorldScene.Result scene, Rac1PickupRandom random, Rac1LootPorts loot,
        Rac1BreakablePorts breakables, Rac1WorldResetPorts reset, Rac1WorldActorPorts actors, Rac1WorldTickPorts ticks,
        Rac1PlayerGameplayPorts player, Rac1WorldContactPorts contacts, Rac1AuthoredMechanismPorts authored, Rac1GameplayHudPorts hud,
        Rac1MetalDetectorPorts detector, Rac1WeaponTargetPorts weaponTargets,
        Rac1WeaponContactPhysicsPorts contactPhysics, Rac1SuckCannonPorts suckCannon, Rac1ArsenalMissilePorts missiles, Rac1ArsenalDeployablePorts deployables, Rac1WalloperPorts walloper, Rac1TaunterPorts taunter, Rac1TeslaPorts tesla, Rac1TaxiHostPorts taxis, Func<Rac1WorldModule, Rac1EnemyHost> enemies)
    {
        _world = world; _scene = scene; _reset = reset; _actors = actors; _ticks = ticks;
        _playerPorts = player; _contacts = contacts; _binding = new(player);
        Context = new(world.LevelId, random);
        Loot = new(scene, Context, Presentation, loot);
        Mechanisms = new(world, scene, authored);
        Weapons = new(detector, weaponTargets, contactPhysics, suckCannon, missiles, deployables, walloper, taunter, tesla);
        Taxis = new(taxis);
        Enemies = enemies(this);
        Breakables = new(world, Context, Presentation, Loot, Enemies, () => breakables.Current(this), breakables);
        Hud = new(world, Context, Presentation, hud, () => _disposed);
    }

    internal Rac1LootPresentation Loot { get; }
    internal Rac1BreakableHost Breakables { get; }
    internal Rac1WorldContext Context { get; }
    internal Rac1AuthoredMechanismRegistry Mechanisms { get; }
    internal Rac1WorldPresentation Presentation { get; } = new();
    internal Rac1GameplayHudPresenter Hud { get; }
    internal Rac1EnemyHost Enemies { get; }
    internal Rac1MetropolisTaxiHost Taxis { get; }
    internal Rac1WeaponHost Weapons { get; }

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

    private void ResetBindings(bool retiring = false)
    {
        // Release the taxi's captured player before its player/world bindings retire.
        Taxis.Reset();
        Weapons.ResetWorld();
        _reset.Player();
        Context.EquipmentUsePressed = false;
        _reset.Wheel();
        Breakables.Reset();
        _reset.Checkpoints();
        _reset.Missions();
        _reset.Wrench();
        _reset.Retry();
        if (retiring) Enemies.Dispose(); else Enemies.ResetRac1Enemies();
        Loot.ResetRac1PickupPresentation();
        Loot.ResetRac1GoldBolts();
        if (retiring) Mechanisms.Dispose();
        Presentation.Dispose();
    }

    internal void Arm(PlayerHost player)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _binding.Arm(player);
        _player = player;
        Mechanisms.OnVisualTickSkipped();
        Mechanisms.BindPlayer(player);
        BindSourcePlayerContacts(player);
    }

    private void BindSourcePlayerContacts(PlayerHost player)
    {
        if (_world.Game != ObpSourceGame.Rac1) return;
        player.Rac1CrankInput = (held, stick) => !_playerPorts.Health().Probe().IsDead && Mechanisms.TryTurnCrank(held, stick);
        player.Rac1WaterSurfaceHeight = Mechanisms.SampleWaterHeight;
        player.Rac1NovalisDrowned += _contacts.Drowned;
        player.Rac1WaterSoundRequested += _contacts.WaterSound;
        player.ConfigureRac1TraversalTargets(_world.DynamicObjects ?? []);
    }

    internal void Tick(double delta)
    {
        if (_disposed) return;
        if (_ticks.Suspended() || _world.Game != ObpSourceGame.Rac1 ||
            _player is null || !GodotObject.IsInstanceValid(_player))
        {
            Mechanisms.OnVisualTickSkipped();
            return;
        }
        if (!_playerPorts.Persistent() && !_binding.TickParityLifecycle())
        {
            Mechanisms.OnVisualTickSkipped();
            return;
        }
        if (!_playerPorts.Alive())
        {
            Taxis.AbortRide();
            Mechanisms.OnVisualTickSkipped();
            return;
        }
        Taxis.Tick(delta);
        Mechanisms.Tick(delta, Rac1AuthoredTickPhase.Music);
        _ticks.Swing(delta);
        if (!_playerPorts.Ordinary()) Enemies.TickRac1Hostiles(delta);
        Mechanisms.Tick(delta, Rac1AuthoredTickPhase.Visual);
        Breakables.TickExplosives(delta);
        _ticks.Checkpoints();
        Weapons.Tick(delta);
        Loot.TickRac1Pickups(delta);
        Loot.TickRac1GoldBolts();
        _ticks.Missions(delta);
    }

    internal void PhysicsStep(bool suspended, bool waitingForReplay) =>
        Mechanisms.PhysicsStep(suspended, waitingForReplay);

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
            // A world swap may unbind the scene player before the ordinary reset.
            // Return its controls/camera and release active taxi collision first.
            Taxis.AbortRide();
            _binding.Dispose();
            ResetBindings(retiring: true);
            _reset.AdditionalWeapons();
            _reset.BombPresentation();
        }
        finally
        {
            _player = null;
            try { Enemies.Dispose(); }
            finally
            {
                try { Taxis.Dispose(); }
                finally
                {
                    try { Weapons.Dispose(); }
                    finally
                    {
                        try { Breakables.Dispose(); }
                        finally
                        {
                            try { Loot.Dispose(); }
                            finally
                            {
                                try { Presentation.Dispose(); }
                                finally
                                {
                                    Context.BombGlove = null;
                                    Context.BombFireRequested = false;
                                    Context.CombatStatus = "off";
                                    _disposed = true;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private void ConfigureActors(RuntimeWorld world, RuntimeWorldScene.Result result)
    {
        if (world.Game != ObpSourceGame.Rac1)
        {
            return;
        }

        Loot.ConfigureRac1PickupPresentation(world);
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
            Loot.AddRac1CrateCollider(node);
        }

        Breakables.ConfigureExplosives(world, result);
        _actors.Checkpoints(world);
        _actors.Missions(world, result);
        Weapons.Configure(world);
        Loot.ConfigureRac1PlacedBolts(world, result);
        Loot.ConfigureRac1GoldBolts(world, result);
        Mechanisms.Configure();
        Breakables.ConfigureNovalis(world, result);

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

        Enemies.ConfigureRac1Enemies(world, result);
        Taxis.Configure(world, result);
        GD.Print($"[rac1-gameplay] ready: {Presentation.CrateNodes.Count} admitted retail crates, " +
                 $"{Presentation.HostileNodes.Count} recovered Veldin class-749 placements from " +
                 $"{authoredClass749} authored placements ({rejectedHostiles} rejected)");
    }

}