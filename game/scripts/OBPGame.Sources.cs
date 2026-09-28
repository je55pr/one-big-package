using Godot;
using OBP.Core;
using OBP.IO;
using OBP.PS2;

namespace OneBigPackage;

public partial class OBPGame
{
    private ObpSourceLibrary _sources = null!;
    private bool _sourcesInitialized;
    private SourceManagerUi? _sourceManager;
    private string _sourceConfigPath = string.Empty;
    private readonly List<string> _sourceRestoreWarnings = new();

    /// <summary>Deferred startup hook for the trilogy Game Sources screen.</summary>
    public void ShowSourceManagerFromBootstrap()
    {
        EnsureSourceLibraryInitialized();
        BuildSourceManagerScreen();
    }

    /// <summary>
    /// Attach a direct source argument to the shared source library. All world
    /// entry remains provider/destination based after the source is attached.
    /// </summary>
    public void RememberCommandLineSource(ObpSourceGame game, string path)
    {
        EnsureSourceLibraryInitialized();
        AttachExpectedSource(game, path, persist: true, reportStatus: false);
    }

    /// <summary>
    /// Esc on the neutral Worlds browser returns to Game Sources before world
    /// input handlers see the key.
    /// </summary>
    public bool TryReturnSelectorToSources()
    {
        if (_mode != Mode.Selector || _args.CaptureFrame is not null)
        {
            return false;
        }

        _genericNavigationActive = false;
        _destinationSelector?.QueueFree();
        _destinationSelector = null;
        BuildSourceManagerScreen();
        return true;
    }

    private void EnsureSourceLibraryInitialized()
    {
        if (_sourcesInitialized)
        {
            return;
        }

        _sources = new ObpSourceLibrary(TrilogySourceDefinitions.All);
        _sourceConfigPath = ProjectSettings.GlobalizePath("user://sources.json");
        EnsureRac1CampaignPersistenceInitialized();
        _sourcesInitialized = true;
        RestoreRememberedSources();
    }

    private void RestoreRememberedSources()
    {
        _sourceRestoreWarnings.Clear();
        IReadOnlyList<ObpSavedSource> saved;
        try
        {
            saved = ObpSourceLibrary.LoadConfig(_sourceConfigPath);
        }
        catch (Exception ex)
        {
            _sourceRestoreWarnings.Add($"Could not read remembered game sources: {ex.Message}");
            return;
        }

        foreach (var entry in saved)
        {
            if (!File.Exists(entry.Path))
            {
                _sourceRestoreWarnings.Add($"Remembered {entry.Game} source is missing: {entry.Path}");
                continue;
            }

            try
            {
                using var reader = new FileRandomAccessReader(entry.Path);
                var attached = _sources.Restore(entry, reader);
                GD.Print($"[sources] restored {attached.Definition.DisplayName}: {attached.DiscSerial} · {attached.Identity.BuildId}");
            }
            catch (Exception ex)
            {
                _sourceRestoreWarnings.Add($"Could not restore {entry.Game} source '{entry.Path}': {ex.Message}");
            }
        }
    }

    private bool AttachExpectedSource(ObpSourceGame expectedGame, string path, bool persist = true, bool reportStatus = true)
    {
        EnsureSourceLibraryInitialized();
        var expected = _sources.Definition(expectedGame);
        try
        {
            using var reader = new FileRandomAccessReader(path);
            var probe = _sources.Probe(reader);
            if (probe.Definition is { } detected && detected.Game != expectedGame)
            {
                string wrong = $"That disc is {detected.DisplayName} ({probe.DiscSerial}), not {expected.DisplayName}.";
                GD.PrintErr($"[sources] {wrong}");
                if (reportStatus)
                {
                    _sourceManager?.SetStatus(wrong, error: true);
                }
                return false;
            }

            if (!probe.Supported)
            {
                string problem = probe.Problem ?? $"The selected file is not the supported {expected.DisplayName} authority build.";
                GD.PrintErr($"[sources] {problem}");
                if (reportStatus)
                {
                    _sourceManager?.SetStatus(problem, error: true);
                }
                return false;
            }

            var attached = _sources.Attach(path, reader);
            if (persist)
            {
                _sources.SaveConfig(_sourceConfigPath);
            }

            _sourceManager?.Refresh(_sources);
            string ok = $"✓ {attached.Definition.DisplayName} attached — {attached.DiscSerial} · {attached.Identity.BuildId}";
            GD.Print($"[sources] {ok} path={attached.Path}");
            if (reportStatus)
            {
                _sourceManager?.SetStatus(ok);
            }
            return true;
        }
        catch (Exception ex)
        {
            string problem = $"Could not attach {expected.DisplayName}: {ex.Message}";
            GD.PrintErr($"[sources] {problem}");
            if (reportStatus)
            {
                _sourceManager?.SetStatus(problem, error: true);
            }
            return false;
        }
    }

    private void BuildSourceManagerScreen()
    {
        EnsureSourceLibraryInitialized();
        _mode = Mode.Picker;
        _sceneKind = "sources";
        TeardownWorld();
        EnsurePlainEnvironment();

        _destinationSelector?.QueueFree();
        _destinationSelector = null;
        if (_worldHud is not null && IsInstanceValid(_worldHud))
        {
            _worldHud.Visible = false;
        }

        // Production startup no longer constructs the smoke fixture. Keep this
        // defensive cleanup for explicit debug/test fixtures so source browsing
        // cannot inherit their sun, floor, cube or banner. World loads are owned
        // by the runtime-world presentation path.
        foreach (var child in _worldRoot.GetChildren())
        {
            if (!child.IsQueuedForDeletion())
            {
                child.QueueFree();
            }
        }
        GetNodeOrNull<DirectionalLight3D>("Sun")?.QueueFree();
        _ui.GetNodeOrNull<Label>("Banner")?.QueueFree();

        _camera.Current = true;
        _activeCamera = _camera;
        Input.MouseMode = Input.MouseModeEnum.Visible;

        _sourceManager?.QueueFree();
        _sourceManager = new SourceManagerUi();
        _sourceManager.SelectRequested += OpenSourcePicker;
        _sourceManager.ForgetRequested += ForgetSource;
        _sourceManager.BrowseWorldsRequested += BrowseWorlds;
        _sourceManager.ControllerSettingsRequested += ShowControllerSettingsFromMenu;
        AddChild(_sourceManager);
        _sourceManager.Populate(_sources, TrilogyWorldProviders.Games);

        if (_sourceRestoreWarnings.Count > 0)
        {
            _sourceManager.SetStatus("⚠ " + string.Join("\n", _sourceRestoreWarnings), error: true);
        }

        GD.Print($"[sources] source manager shown — {_sources.Attached.Count}/3 sources attached");
    }

    private void OpenSourcePicker(ObpSourceGame game)
    {
        var definition = _sources.Definition(game);
        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = $"Select {definition.DisplayName} disc image",
            UseNativeDialog = true,
        };
        dialog.AddFilter("*.iso", "PS2 disc image");
        dialog.FileSelected += path =>
        {
            dialog.QueueFree();
            AttachExpectedSource(game, path);
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(1000, 640));
    }

    private void ForgetSource(ObpSourceGame game)
    {
        if (!_sources.Remove(game))
        {
            return;
        }

        try
        {
            _sources.SaveConfig(_sourceConfigPath);
            _sourceManager?.Refresh(_sources);
            _sourceManager?.SetStatus($"Forgot {TrilogySourceDefinitions.All.First(d => d.Game == game).DisplayName}. The retail file itself was not changed.");
        }
        catch (Exception ex)
        {
            _sourceManager?.SetStatus($"Source was detached for this session, but the remembered-source config could not be saved: {ex.Message}", error: true);
        }
    }
}
