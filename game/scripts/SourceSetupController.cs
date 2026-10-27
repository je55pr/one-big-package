using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.PS2;

namespace OBP.Game;

internal sealed record SourceSetupNavigation(Action InitializePersistence, Action PrepareSetupScene, Action ShowTitle,
    Action ShowWorlds, Action ShowControllerSettings, Action AbandonTravel, Action<string, int> Quit);
internal sealed record SourceSetupNotices(string? Profile, bool ProfileBlocked, string? Campaign);

/// <summary>Setup-only UI, picker and readiness routing. The existing scene/player owners remain behind navigation ports.</summary>
internal sealed class SourceSetupController(Node owner, SourceSetupCoordinator setup, SourceSetupNavigation navigation,
    Func<SourceSetupNotices> notices, Func<bool> debug, CancellationToken lifetime, bool automated) : IDisposable
{
    private SourceManagerUi? _view;
    private readonly List<FileDialog> _dialogs = [];
    private long _viewVersion;
    private bool _initialized, _disposed;
    public SourceSetupCoordinator Setup { get; } = setup;
    public bool HasView => GodotObject.IsInstanceValid(_view) && !_view!.IsQueuedForDeletion();
    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        navigation.InitializePersistence();
        Setup.Initialize();
    }
    public bool InitializeStartup(CommandLineArgs args)
    {
        Initialize();
        bool persist = !args.ControllerSmoke && !args.Rac1PlayerAudioSmoke && !args.BugReportSmoke && args.ReplayBugReport is null;
        if (Setup.ResolveStartup([(ObpSourceGame.Rac1, args.Rac1Iso), (ObpSourceGame.Rac2, args.Rac2Iso), (ObpSourceGame.Rac3, args.Rac3Iso)], persist)) return true;
        navigation.Quit("source-argument-invalid", 2); return false;
    }
    public void ShowForStartup()
    {
        Initialize();
        if (Setup.RequireOrdinary(out _) && Setup.Access == SourceAccessMode.OrdinaryPlay) navigation.ShowTitle();
        else Show();
    }
    public bool RequireOrdinary(string reason)
    {
        Initialize();
        if (Setup.RequireOrdinary(out var readiness)) return true;
        GD.PrintErr("[setup] Trilogy setup required before " + reason + ": " + string.Join("; ",
            readiness.Entries.Where(entry => !entry.Ready).Select(entry => entry.DisplayName + ": " + entry.Problem)) +
            (Setup.RepairNeeded ? "; remembered configuration needs explicit repair" : ""));
        navigation.AbandonTravel(); Show();
        if (automated) navigation.Quit("trilogy-setup-required", 2);
        return false;
    }
    public void Tick(double delta, bool ordinarySurface)
    {
        if (Setup.Due(delta, ordinarySurface)) RequireOrdinary("continued play");
    }
    public void Remember(ObpSourceGame game, string path, bool persist = true) => Attach(game, path, persist, false);
    private void Attach(ObpSourceGame game, string path, bool persist = true, bool reportStatus = true)
    {
        Initialize();
        var result = Setup.Attach(game, path, persist);
        if (HasView) _view!.Refresh(Setup.Library);
        if (reportStatus)
        {
            string message = result.Accepted && !debug() ? $"✓ {Setup.Library.Definition(game).DisplayName} attached" : result.Message;
            SetStatus(result.Warning is null ? message : message + "\n" + result.Warning, !result.Accepted || result.Warning is not null);
        }
        if (Setup.ConsumersInvalidated && Setup.Access == SourceAccessMode.OrdinaryPlay) RequireOrdinary("source replacement");
    }
    private void Forget(ObpSourceGame game)
    {
        var result = Setup.Forget(game);
        if (HasView) _view!.Refresh(Setup.Library);
        if (result.Message.Length > 0) SetStatus(result.Message, !result.Accepted);
    }
    private void Complete()
    {
        var result = Setup.Complete();
        if (!result.Accepted) { SetStatus(result.Message, true); return; }
        navigation.ShowWorlds();
    }
    public void Show()
    {
        if (_disposed || lifetime.IsCancellationRequested) return;
        Initialize();
        navigation.PrepareSetupScene();
        var world = owner.GetNode<Node3D>("WorldRoot");
        foreach (var child in world.GetChildren()) if (!child.IsQueuedForDeletion()) child.QueueFree();
        owner.GetNodeOrNull<DirectionalLight3D>("Sun")?.QueueFree();
        owner.GetNode<CanvasLayer>("UI").GetNodeOrNull<Label>("Banner")?.QueueFree();
        ApplicationPointer.MouseMode = Input.MouseModeEnum.Visible;
        RetireView();
        _view = new SourceManagerUi
        {
            DebugOutputEnabled = debug(),
            Readiness = Setup.Readiness,
            DiagnosticSources = Setup.Access == SourceAccessMode.ExplicitDiagnostic,
            ConfigNeedsRepair = () => Setup.RepairNeeded,
        };
        _view.SelectRequested += OpenPicker; _view.ForgetRequested += Forget; _view.BrowseWorldsRequested += Complete;
        _view.ControllerSettingsRequested += navigation.ShowControllerSettings;
        _view.QuitRequested += () => navigation.Quit("source-menu-quit", 0);
        owner.AddChild(_view); _view.Populate(Setup.Library, TrilogyWorldProviders.Games);
        ShowNotices();
        GD.Print($"[sources] source manager shown — {Setup.Library.Attached.Count}/3 sources attached");
    }
    private void ShowNotices()
    {
        var external = notices(); var messages = new List<string>();
        if (external.Profile is { } profile) messages.Add(profile);
        if (external.Campaign is { } campaign) messages.Add(campaign);
        if (Setup.ConfigurationWarning is { } config) messages.Add("⚠ " + config);
        if (Setup.RestoreWarnings.Count > 0) messages.Add("⚠ " + string.Join("\n", Setup.RestoreWarnings.Values));
        if (messages.Count > 0) SetStatus(string.Join("\n", messages), external.ProfileBlocked || external.Campaign is not null ||
            Setup.ConfigurationWarning is not null || Setup.RestoreWarnings.Count > 0);
    }
    private void OpenPicker(ObpSourceGame game)
    {
        if (!HasView || _disposed || lifetime.IsCancellationRequested) return;
        long version = _viewVersion;
        var returnFocus = owner.GetViewport().GuiGetFocusOwner();
        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = $"Select {Setup.Library.Definition(game).DisplayName} disc image",
            UseNativeDialog = false,
        };
        _dialogs.Add(dialog); dialog.AddFilter("*.iso", "PS2 disc image");
        dialog.FileSelected += path =>
        {
            CloseDialog(dialog);
            if (_disposed || lifetime.IsCancellationRequested || version != _viewVersion) return;
            Attach(game, path); MenuFocus.Restore(returnFocus, []);
        };
        dialog.Canceled += () => { CloseDialog(dialog); if (!_disposed && version == _viewVersion) MenuFocus.Restore(returnFocus, []); };
        owner.AddChild(dialog); dialog.AddChild(new ControllerPopupInput());
        dialog.PopupCenteredClamped(new Vector2I(1000, 640));
        Callable.From(() =>
        {
            if (!_disposed && version == _viewVersion && GodotObject.IsInstanceValid(dialog) && !dialog.IsQueuedForDeletion())
                dialog.FindChildren("*", "Tree", recursive: true, owned: false).OfType<Tree>().FirstOrDefault()?.GrabFocus();
        }).CallDeferred();
    }
    private void CloseDialog(FileDialog dialog)
    {
        _dialogs.Remove(dialog);
        if (GodotObject.IsInstanceValid(dialog) && !dialog.IsQueuedForDeletion()) dialog.QueueFree();
    }
    public void SetStatus(string text, bool error = false) { if (HasView) _view!.SetStatus(text, error); }
    public void SetVisible(bool visible)
    {
        if (!HasView) return;
        _view!.Visible = visible;
        if (visible) _view.RestoreFocus();
    }
    public void RetireView()
    {
        _viewVersion++;
        foreach (var dialog in _dialogs.ToArray()) CloseDialog(dialog);
        if (HasView) _view!.QueueFree();
        _view = null;
    }
    public void Dispose() { if (_disposed) return; _disposed = true; RetireView(); }
}
