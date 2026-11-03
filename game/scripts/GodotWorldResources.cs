using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Game.Diagnostics;
using OBP.Godot;
using OBP.IO;
using OBP.RAC2.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Presentation;

namespace OBP.Game;

/// <summary>Godot resources for one prepared or adopted scene, including a bound transient player node.</summary>
internal sealed class GodotWorldResources(RuntimeWorldScene.Result prepared) : IDisposable
{
    private PlayerHost? _player;
    private bool _disposed;
    public WorldHost Presentation { get; } = new();
    public RuntimeWorldScene.Result Scene { get; } = prepared;
    // Native rewards follow the world lease. The optional probe only observes them.
    public Rac2FreshBoltSession Rac2CrateBolts { get; } = new();
    public GodotRac2CrateDiagnosticFixture CrateDiagnostics { get; } = new(prepared.DynamicObjectNodes ?? []);
    public WorldEffectHost? Effects { get; set; }
    public string? EffectsFailure { get; set; }
    public WorldEntryRequest? EffectScope { get; set; }
    public Rac1WorldModule? Rac1Gameplay { get; set; }
    public Rac2WorldModule? Rac2Gameplay { get; set; }
    public Rac3WorldModule? Rac3Gameplay { get; set; }
    public IDisposable? FusionWeapons { get; set; }
    public IDisposable? PlayerLife { get; set; }
    internal GameplayTickScheduler<InputEvent>? GameplayTicks { get; private set; }

    internal sealed record ActivationBindings(
        Node3D Host, GodotApplicationPresentation Application, Func<CommandLineArgs> Args,
        PlayerAvatarPresentationHost Avatar, PlayerEquipmentPresentationBinder Equipment,
        Func<bool> Ordinary, Action<(long Length, long Written)?> SetAvatarStamp,
        GodotPlayerLifecycleHost PlayerLife, ApplicationNavigationCoordinator Navigation,
        SourceSetupController Sources,
        Func<RuntimeWorld, RuntimeWorldScene.Result, Rac1WorldModule> CreateRac1,
        Func<RuntimeWorld, GodotWorldResources, Rac2WorldModule> CreateRac2,
        Action<RuntimeWorld> SpawnPlayer, ActorWorldHost Actors, Rac1CampaignHost Campaign,
        Func<Node3D, IDisposable> EquipmentWorld, Func<int> CountActivation, Action<RuntimeSpawn> OverrideEntry, Action SetWorldMode,
        Action<string> Persist, HudStateAdapter Hud,
        EffectBindings Effects, WorldGameplayTickPorts Ticks,
        Action<int> EffectsSmoke, Action<string> PublishSummary, Func<long> PlayerGeneration);

    internal void Activate(WorldSceneLease<GodotWorldResources> entry, ActivationBindings bindings)
    {
        if (bindings.Avatar.HasPrepared && bindings.Avatar.ApplyPrepared()) bindings.Equipment.InvalidateSource();
        bindings.SetAvatarStamp(bindings.Avatar.CommittedSourceStamp);
        if (bindings.Ordinary()) bindings.Equipment.ApplyPreparedBank();
        EffectScope = entry.Plan.Request;
        var application = bindings.Application;
        GodotSmokeSceneFixture.RetireBootstrap(bindings.Host, application.WorldRoot, application.Ui);
        PlayerLife = bindings.PlayerLife.BindWorld(entry.Plan.Request.WorldInstanceId, entry.Plan.Request.Generation, Scene.Root);
        bindings.Navigation.RetireMenuPages();
        bindings.Sources.RetireView();
        var world = entry.Plan.Prepared.World;
        Rac1Gameplay = bindings.CreateRac1(world, Scene);
        Rac2Gameplay = bindings.CreateRac2(world, this);
        Rac3Gameplay = Rac3WorldModule.CreateForWorld(world, Scene);
        int switches = bindings.CountActivation();
        if (bindings.Campaign.BeginWorldEntry(world) is { } authoredEntry) bindings.OverrideEntry(authoredEntry);
        var args = bindings.Args();
        Load(bindings.Host, application.WorldRoot, world, entry.Plan.Request.DestinationId,
            WorldEntryPreparationPolicy.WorldOptions(world, args));
        application.SetupOverlay(Scene, world, Presentation, args.Overlay, args.Inspect);
        Rac2Gameplay?.Configure();
        bool staticCamera = args.ShotsPath is not null || args.CaptureFrame is not null &&
            (args.DirectLoad || args.Destination is not null) && args.TestScene != "player" &&
            !args.AnimSolo && !args.CrateFocus && !args.CrateAutoStrike;
        if (!staticCamera)
        {
            Rac1Gameplay.Configure();
            Rac3Gameplay?.Configure();
        }
        FusionWeapons = bindings.EquipmentWorld(Scene.Root);
        if (args.AnimSolo)
        {
            GodotWorldCaptureCamera.FrameAnimated(application.Camera, world);
            application.ActiveCamera = application.Camera;
        }
        else if (staticCamera)
        {
            GodotWorldCaptureCamera.FrameShowcase(application.Camera, world);
            application.ActiveCamera = application.Camera;
        }
        else
        {
            bindings.SpawnPlayer(world);
            if (application.Player is { } player) Rac2Gameplay?.Arm(player);
        }
        bindings.SetWorldMode();
        bindings.Navigation.WorldActivated(args.CaptureFrame is null);
        if (entry.Lab is not null) bindings.Navigation.DecorateLab(entry.Lab, bindings.Avatar.View is not null);
        bindings.Actors.Configure(bindings.Ordinary(), bindings.PlayerLife.WorldId, bindings.PlayerLife.Generation, entry.Lab, Scene.Root);
        BindEffects(bindings.Effects);
        application.EnsurePlayerHud(); application.EnsureWorldHud();
        if (bindings.Ordinary()) bindings.PlayerLife.PublishHud();
        application.UpdatePlayerHud(bindings.Hud.Current);
        application.UpdateWorldHud(entry, Presentation, switches, true);
        if (bindings.Campaign.FinishWorldEntry(world)) bindings.Persist(Rac1CampaignPersistenceHost.TravelPersistenceReason);
        bindings.EffectsSmoke(switches);
        string summary = $"✓ {entry.NativeDestination?.DisplayName ?? (entry.Lab is not null ? "Fusion Lab" : world.DisplayName)} · {Scene.MeshInstances} meshes";
        bindings.PublishSummary(summary);
        GD.Print($"[destinations] world ready — {summary} (switch #{switches}, player generation {bindings.PlayerGeneration()})");
        BindPlayer(application.Player);
        WorldGameplayTickBindings.Activate(entry, bindings.Ticks);
    }

    internal void BindGameplayTicks(GameplayTickScheduler<InputEvent> ticks)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (GameplayTicks is not null) throw new InvalidOperationException("World ticks already bound.");
        GameplayTicks = ticks;
    }

    internal void RetireGameplayTicks()
    {
        GameplayTicks?.Dispose();
        GameplayTicks = null;
    }

    private void DisposeSourceGameplay()
    {
        try { Rac3Gameplay?.Dispose(); }
        finally
        {
            Rac3Gameplay = null;
            try { Rac1Gameplay?.Dispose(); }
            finally { Rac1Gameplay = null; }
        }
    }

    internal void RetireGameplayBeforePlayer()
    {
        try { Rac1Gameplay?.Dispose(); }
        finally { Rac1Gameplay = null; }
        try { Rac2Gameplay?.Dispose(); }
        finally { Rac2Gameplay = null; }
    }

    internal void RetireGameplayAfterPlayer()
    {
        try { Rac3Gameplay?.Dispose(); }
        finally { Rac3Gameplay = null; }
    }

    // Only the scene lease may publish and retire this world's effects. The host
    // supplies narrow callbacks; it does not construct effect resources itself.
    internal sealed record EffectBindings(
        Func<Camera3D?> Camera,
        Func<IReadOnlyList<ObpAttachedSource>> Sources,
        Func<ActorInstanceKey, bool> ActorAlive,
        Func<System.Numerics.Vector3> PlacementOffset,
        Func<bool> Suspended,
        Action<string> RecordOperation,
        Func<bool> CommandAllowed,
        Action<string> LogError);

    internal void BindEffects(EffectBindings bindings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Effects is not null || EffectsFailure is not null)
            throw new InvalidOperationException("This world already attempted to bind effects.");
        var scope = EffectScope ?? throw new InvalidOperationException("Effects require the existing world lease.");
        try
        {
            Effects = new WorldEffectHost(Scene.Root, scope.WorldInstanceId, scope.Generation,
                bindings.Camera, bindings.Sources, bindings.ActorAlive, bindings.PlacementOffset,
                bindings.Suspended, bindings.RecordOperation, bindings.CommandAllowed);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
        {
            EffectsFailure = "Native effects setup is unavailable (" + error.GetType().Name + "). Check the experiment definition.";
            bindings.LogError("[effects] " + EffectsFailure);
        }
    }

    public void Load(Node owner, Node3D parent, RuntimeWorld world, string name, WorldHost.Options options) =>
        Presentation.Load(owner, parent, world, name, options, Scene);

    public void BindPlayer(PlayerHost? player) => _player = player;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RetireGameplayTicks();
        try
        {
            try
            {
                try { DisposeSourceGameplay(); }
                finally
                {
                    try { Rac2Gameplay?.Dispose(); }
                    finally
                    {
                        Rac2Gameplay = null;
                        try { PlayerLife?.Dispose(); }
                        finally
                        {
                            try { FusionWeapons?.Dispose(); }
                            finally { Effects?.Dispose(); }
                        }
                    }
                }
            }
            finally
            {
                Effects = null;
                if (GodotObject.IsInstanceValid(_player))
                {
                    if (_player!.GetParent() is { } parent) parent.RemoveChild(_player);
                    if (!_player.IsQueuedForDeletion()) _player.QueueFree();
                }
            }
        }
        finally
        {
            // Load can fail after allocating the environment but before attaching
            // the staged root. Both parts must be retired even on partial load.
            bool ownedByPresentation = Presentation.Result is not null;
            Presentation.Unload();
            if (!ownedByPresentation && GodotObject.IsInstanceValid(Scene.Root) && !Scene.Root.IsQueuedForDeletion())
            {
                if (Scene.Root.IsInsideTree()) Scene.Root.QueueFree();
                else Scene.Root.Free();
            }
        }
    }
}
