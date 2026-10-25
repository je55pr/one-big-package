using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.PS2;

namespace OneBigPackage;

public partial class OBPGame
{
    private SourceReadinessEvaluator _sourceReadiness = null!;
    private bool _startupSourcesResolved, _diagnosticSourceAccess, _sourceConfigNeedsRepair, _setupProfileSuspended;
    private double _sourceCheckElapsed;
    internal bool ProfileRecoveryRequired => _profileWriteBlocked;

    private bool InitializeStartupSources()
    {
        if (_startupSourcesResolved) return true;
        _diagnosticSourceAccess = SourceSetupPolicy.IsDiagnostic(_args, OS.GetCmdlineUserArgs(), OS.GetCmdlineArgs());
        EnsureSourceLibraryInitialized();
        foreach (var (game, path) in new[] { (ObpSourceGame.Rac1, _args.Rac1Iso), (ObpSourceGame.Rac2, _args.Rac2Iso), (ObpSourceGame.Rac3, _args.UyaIso) })
        {
            if (path is null) continue;
            bool persist = !_args.ControllerSmoke && !_args.Rac1PlayerAudioSmoke && !_args.BugReportSmoke && _args.ReplayBugReport is null;
            if (AttachExpectedSource(game, path, persist, reportStatus: false)) continue;
            _setupProfileSuspended = true;
            ApplicationLifecycle.RequestQuit(this, "source-argument-invalid", 2);
            return false;
        }
        _startupSourcesResolved = true;
        return true;
    }

    public bool RequireStartupSetup() => RequireOrdinarySourceSetup("startup");

    private bool RequireOrdinarySourceSetup(string reason)
    {
        EnsureSourceLibraryInitialized();
        if (_diagnosticSourceAccess) return true;
        var readiness = _sourceReadiness.Evaluate();
        if (readiness.Ready && !_sourceConfigNeedsRepair)
        {
            _setupProfileSuspended = false;
            return true;
        }
        _setupProfileSuspended = true;
        GD.PrintErr("[setup] Trilogy setup required before " + reason + ": " + string.Join("; ", readiness.Entries.Where(entry => !entry.Ready).Select(entry => entry.DisplayName + ": " + entry.Problem)) +
            (_sourceConfigNeedsRepair ? "; remembered configuration needs explicit repair" : ""));
        AbandonFailedRac1Travel();
        BuildSourceManagerScreen();
        if (DisplayServer.GetName() == "headless" || AutomationRunPolicy.IsAutomated(OS.GetCmdlineUserArgs(), OS.GetCmdlineArgs()))
            ApplicationLifecycle.RequestQuit(this, "trilogy-setup-required", 2);
        return false;
    }

    private void CompleteSourceSetup()
    {
        if (!_diagnosticSourceAccess && _sourceConfigNeedsRepair && _sourceReadiness.Evaluate().Ready)
        {
            try
            {
                _sources.SaveConfig(_sourceConfigPath);
                _sourceConfigNeedsRepair = false;
                _sourceConfigWarning = null;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
            {
                _sourceManager?.SetStatus("Could not repair the remembered configuration: " + error.Message, error: true);
                return;
            }
        }
        ShowDestinationSelector();
    }

    private void TickSourceSetup(double delta)
    {
        if (_diagnosticSourceAccess || !_startupSourcesResolved || _mode is not (Mode.World or Mode.Selector or Mode.Title)) return;
        _sourceCheckElapsed += delta;
        if (_sourceCheckElapsed < 1) return;
        _sourceCheckElapsed = 0;
        RequireOrdinarySourceSetup("continued play");
    }
}
