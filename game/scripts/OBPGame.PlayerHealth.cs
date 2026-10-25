using Godot;
using OBP.Fusion;
using OBP.Godot;
using OBP.Runtime.Gameplay;

namespace OneBigPackage;

public partial class OBPGame
{
    private readonly HashSet<ActorInstanceKey> _playerDamageActors = [];
    private string? _playerDamageWorld;
    private long _playerDamageLease;
    private long _playerServiceTick;
    private bool PlayerAlive => _playerNative?.Life.Alive ?? !_rac1Nanotech.Probe().IsDead;

    private void BeginPlayerHealthWorld(string world, long lease)
    {
        _playerDamageActors.Clear();
        _playerEmittedRewardIndex = 0;
        _playerDamageWorld = world; _playerDamageLease = lease;
        _playerNative?.Life.BeginWorld(world, lease);
    }

    private void StepPlayerServicesPhysics()
    {
        _playerServiceTick++;
        if (_playerNative is null || _player is null) return;
        _playerNative.Life.TickNative();
        // Explicit OBP policy over an authored world fact; this is not a
        // recovered GC/UYA death admission or a native campaign restart.
        if (_world?.Environment is { } environment && _player.GlobalPosition.Y < environment.DeathHeight)
            _playerNative.Life.DefeatFromWorldHazard();
        _player.PlayerHealth = _playerNative.Life.Capture();
        _player.Rac1GameplayState = _playerNative.Health.Probe();
        var health = _player.PlayerHealth;
        if (_hudState.Current.Health is not { } displayed || displayed.Current != health.Current || displayed.Capacity != health.Capacity ||
            displayed.LifeState != (health.Alive ? OBP.Runtime.Presentation.HudLifeState.Alive : OBP.Runtime.Presentation.HudLifeState.Dead)) RefreshRac1HudState();
        if (_hudState.Current.Air != OBP.RAC1.Presentation.Rac1HudProjection.CaptureAir(_player.Rac1SwimState,
            _player.Rac1MovementEquipment.OxygenMask)) RefreshRac1HudState();
        if (!PlayerAlive && _rac1CombatRetry is null) { ClearFusionOnDeath(); ResetRac1WrenchHost(); ShowPlayerRetry(); }
    }

    private PlayerIntakeResult ApplyPlayerSourceDamage(RuntimeWorldScene.DynamicObjectNode node, string build, string adapter,
        string descriptor, long sequence, int amount, ulong flags)
    {
        if (_playerDamageWorld is null || _sceneResult is null || !IsInstanceValid(node.Root) || !node.Root.IsInsideTree() ||
            !_sceneResult.Root.IsAncestorOf(node.Root))
            return new(PlayerIntakeStatus.StaleLease, "Attack producer belongs to a retired world scene.", _playerNative!.Life.Capture());
        var source = node.Source;
        var key = new ActorInstanceKey(_playerDamageWorld!, _playerDamageLease,
            new WeaponTargetKey(source.SourceGame, source.NativeClassId, source.InstanceIndex));
        if (_playerDamageActors.Add(key))
        {
            _playerNative!.Life.RegisterActor(new(key,
                new(source.SourceGame, build, $"moby:{source.NativeClassId}"), adapter,
                source.InteractionId, [new(descriptor, CapabilityAdmission.Ready)]),
                () => IsInstanceValid(node.Root) &&
                    (source.SourceGame == "rac1" ? _rac1MobyRuntime.TryGet(new(source.NativeClassId, source.InstanceIndex), out var native) && native!.IsActive :
                        _gcOpeningMsr1.TryGetValue(source.InstanceIndex, out var gc) && !gc.Hostile.IsTerminal));
        }
        var before = _playerNative!.Health.Probe();
        var result = _playerNative.Life.Apply(new(key, build, adapter, descriptor, sequence, amount, flags));
        if (_player is { } player) { player.PlayerHealth = result.Health; player.Rac1GameplayState = _playerNative.Health.Probe(); }
        if (result.Applied)
        {
            NotifyRac1PlayerDamageAudio(before, _playerNative.Health.Probe());
            RefreshRac1HudState();
        }
        return result;
    }

    private void ShowPlayerRetry()
    {
        if (_rac1CombatRetry is not null || _world is null) return;
        var panel = new PanelContainer { Name = "PlayerRetry", Size = new(300, 110) };
        var rows = new VBoxContainer();
        rows.AddChild(new Label { Text = "Defeated", HorizontalAlignment = HorizontalAlignment.Center });
        var retry = new Button { Text = "Respawn" };
        retry.Pressed += () => RespawnPlayer();
        rows.AddChild(retry); panel.AddChild(rows); _ui.AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center); retry.GrabFocus();
        ApplicationPointer.MouseMode = Input.MouseModeEnum.Visible; _rac1CombatRetry = panel;
    }

    private bool RespawnPlayer()
    {
        if (_playerNative is null || _playerSession is null || PlayerAlive) return false;
        var before = _playerSession.Capture();
        // OBP policy: fresh life at this world's entry; wallet, ammo and loadout
        // persist. No native campaign save/checkpoint format is merged.
        _playerNative.Life.Respawn();
        _playerSession.Configure(_playerSession.Capture().Configuration);
        bool entered = _activeDestination is { } native ? OnDestinationChosen(native) : EnterFusionLab();
        if (!entered)
        {
            // A rejected stage must not consume the retry or leave a live
            // player behind a stale defeated panel. Retain the prior life.
            _playerSession.Restore(before);
            if (_player is { } player) { player.PlayerHealth = _playerNative.Life.Capture(); player.Rac1GameplayState = _playerNative.Health.Probe(); }
            RefreshRac1HudState();
        }
        return entered;
    }
}
