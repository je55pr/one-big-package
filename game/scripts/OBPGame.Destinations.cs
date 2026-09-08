using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.Runtime;

namespace OneBigPackage;

public partial class OBPGame
{
    private DestinationSelectorUi? _destinationSelector;
    private ObpDestination? _activeDestination;
    private string? _currentDestinationId;
    private bool _genericNavigationActive;

    /// <summary>Open the neutral world browser from the trilogy source screen.</summary>
    private void BrowseWorlds() => ShowDestinationSelector();

    /// <summary>Deterministic/debug bootstrap entry used by --test-scene worlds.</summary>
    public void ShowDestinationSelectorFromBootstrap()
    {
        EnsureSourceLibraryInitialized();
        ShowDestinationSelector();
    }

    /// <summary>
    /// Resolve a canonical neutral destination id (for example
    /// <c>rac2:LEVEL1</c>) and enter it through the same provider routing path.
    /// Legacy --planet/--gc-level options remain supported separately.
    /// </summary>
    public void OpenDestinationFromBootstrap(string destinationId)
    {
        EnsureSourceLibraryInitialized();
        var destination = TrilogyWorldProviders.Resolve(destinationId);
        if (destination is null)
        {
            GD.PrintErr($"[destinations] unknown destination id '{destinationId}'");
            BuildSourceManagerScreen();
            _sourceManager?.SetStatus($"Unknown destination id '{destinationId}'. Use a canonical id such as rac2:LEVEL1.", error: true);
            return;
        }

        _genericNavigationActive = _args.CaptureFrame is null;
        OnDestinationChosen(destination);
    }

    private void ShowDestinationSelector()
    {
        EnsureSourceLibraryInitialized();
        _mode = Mode.Selector;
        _sceneKind = "destinations";
        _genericNavigationActive = true;
        TeardownWorld();
        EnsurePlainEnvironment();

        _selector?.QueueFree();
        _selector = null;
        _pickerPanel?.QueueFree();
        _pickerPanel = null;
        _sourceManager?.QueueFree();
        _sourceManager = null;
        if (_worldHud is not null && IsInstanceValid(_worldHud))
        {
            _worldHud.Visible = false;
        }

        _camera.Current = true;
        _activeCamera = _camera;
        Input.MouseMode = Input.MouseModeEnum.Visible;

        _destinationSelector?.QueueFree();
        _destinationSelector = new DestinationSelectorUi();
        _destinationSelector.DestinationChosen += OnDestinationChosen;
        _destinationSelector.BackRequested += () =>
        {
            _genericNavigationActive = false;
            _destinationSelector?.QueueFree();
            _destinationSelector = null;
            BuildSourceManagerScreen();
        };
        AddChild(_destinationSelector);
        _destinationSelector.Populate(_sources, TrilogySourceDefinitions.All, TrilogyWorldProviders.All, _currentDestinationId);

        GD.Print($"[destinations] selector shown — {TrilogyWorldProviders.All.Count} runtime provider(s)");
    }

    private void OnDestinationChosen(ObpDestination destination)
    {
        var provider = TrilogyWorldProviders.Find(destination.Game);
        var source = _sources.Get(destination.Game);
        if (provider is null)
        {
            DestinationError($"No production world provider is registered for {destination.Game} yet.");
            return;
        }
        if (source is null)
        {
            DestinationError("That game source is no longer attached. Return to Game Sources and select it again.");
            return;
        }
        if (!provider.CanLoad(destination))
        {
            DestinationError($"Provider {provider.BuildId} rejected destination {destination.DestinationId}.");
            return;
        }

        _currentDestinationId = destination.DestinationId;
        _activeDestination = destination;
        _destinationSelector?.QueueFree();
        _destinationSelector = null;
        _selector?.QueueFree();
        _selector = null;

        EnterProviderWorld(provider, source.Path, destination);
    }

    /// <summary>
    /// Complete game-neutral world-entry path: provider imports native bytes into
    /// RuntimeWorld, then the shared Godot builder renders/collides/spawns it.
    /// No source-game catalogue or native level type is consulted here.
    /// </summary>
    private void EnterProviderWorld(IObpWorldProvider provider, string sourcePath, ObpDestination destination)
    {
        TeardownWorld();
        ClearBootstrapSceneArtifacts();

        string gameSlug = destination.Game.ToString().ToLowerInvariant();
        string placeSlug = destination.PlanetLabel.ToLowerInvariant().Replace(' ', '-');
        _sceneKind = $"{gameSlug}-{placeSlug}";

        RuntimeWorld world;
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            world = TrilogyWorldProviders.Registry.Load(sourcePath, destination);
            sw.Stop();
            GD.Print($"[destinations] imported {destination.DestinationId} / {destination.DisplayName} in {sw.ElapsedMilliseconds} ms — " +
                     $"{world.Meshes.Count} meshes / {world.TotalRenderTriangles:N0} tris / {world.TotalCollisionTriangles:N0} coll tris");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[destinations] import of {destination.DestinationId} failed: {ex.Message}\n{ex.StackTrace}");
            _world = null;
            if (_args.CaptureFrame is not null)
            {
                GetTree().Quit(1);
                return;
            }

            ShowDestinationSelector();
            _destinationSelector?.SetHint($"⚠ {destination.DisplayName} failed to import: {ex.Message}", error: true);
            return;
        }

        AdoptRuntimeWorld(world, sourcePath, destination);
    }

    /// <summary>
    /// Host-only half of world entry. Everything before this point is source-game
    /// parsing; everything from here on is generic RuntimeWorld presentation.
    /// This is the seam future R&C1/UYA providers plug into.
    /// </summary>
    private void AdoptRuntimeWorld(RuntimeWorld world, string sourcePath, ObpDestination destination)
    {
        _world = world;
        _worldSwitches++;

        bool framedCapture = _args.CaptureFrame is not null
            && (_args.DirectLoad || _args.Destination is not null)
            && _args.TestScene != "player"
            && !_args.AnimSolo
            && !_args.CrateFocus && !_args.CrateAutoStrike;

        var result = _worldHost.Load(this, _worldRoot, world, $"World_{destination.Game}_{destination.NativeDestinationId}", new WorldHost.Options
        {
            IncludeMobyMarkers = !(_args.CaptureFrame is not null && !framedCapture),
            IncludeSky = !_args.AnimSolo,
            IncludeCollision = true,
            ShowCollisionDebug = _args.CollisionDebug,
            OnlyAnimatedMobies = _args.AnimSolo,
        });
        _sceneResult = result;
        SetupOverlay(result, world);
        ConfigureCrateDebugHarness();

        if (_args.AnimSolo)
        {
            FrameAnimatedMobies(world);
        }
        else if (framedCapture)
        {
            FrameShowcaseCamera(world);
        }
        else
        {
            SpawnPlayer(world);
        }

        _mode = Mode.World;
        EnsureWorldHud();
        UpdateWorldHud();

        _loadSummary = $"✓ {destination.DisplayName} · {result.MeshInstances} meshes / {result.Triangles:N0} tris / {result.CollisionBodies} colliders";
        GD.Print($"[destinations] world ready — {_loadSummary} (switch #{_worldSwitches})");

        // Exact verification remains game-specific today. Preserve the mature GC
        // opt-in while avoiding an implicit multi-GB hash for other providers.
        if (_args.VerifyHash && _worldSwitches == 1 && destination.Game == ObpSourceGame.Rac2)
        {
            StartBackgroundVerify(sourcePath);
        }
    }

    private void DestinationError(string message)
    {
        GD.PrintErr($"[destinations] {message}");
        if (_destinationSelector is not null)
        {
            _destinationSelector.SetHint(message, error: true);
        }
        else if (_sourceManager is not null)
        {
            _sourceManager.SetStatus(message, error: true);
        }
    }

    /// <summary>
    /// Called in the early input phase so an interactive world entered from the
    /// generic browser returns to Worlds rather than the legacy GC selector.
    /// </summary>
    public bool TryReturnWorldToDestinations()
    {
        if (!_genericNavigationActive || _mode != Mode.World || _args.CaptureFrame is not null)
        {
            return false;
        }

        ShowDestinationSelector();
        return true;
    }
}
