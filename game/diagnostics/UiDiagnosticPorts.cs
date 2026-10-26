using System.Collections.Immutable;
using System.Numerics;
using System.Text.RegularExpressions;
using OBP.Core;
using OBP.Runtime.Player;
using OBP.RAC1.Presentation;

namespace OBP.Game.Diagnostics;

public enum DiagnosticMenuSurface { Title, WorldSelect, Overlay, World, Pause }
public sealed record DiagnosticNavigationSnapshot(bool TitleActive, bool SelectorActive, bool WorldActive, bool RepairActive,
    bool DiagnosticSourceAccess, bool SourceManagerPresent, bool TitlePresent, bool WorldLoaded,
    string? Destination, string? SavedDestination, bool ProfileWriteBlocked, string? SaveNotice,
    bool LabButtonPresent, bool EnabledButtonFocused);
public sealed record DiagnosticStartupPresentation(ImmutableArray<string> VisiblePlaceholders, bool FallbackVisible,
    bool ScenePresent, bool PlayerPresent, bool NativeAvatarPresent, bool PlayerCameraActive, int? LevelId);
public sealed record DiagnosticPlayerContinuity(PlayerSessionConfiguration Configuration, PlayerHealthSnapshot Health,
    string LoadoutJson, string CurrenciesJson);
public sealed record DiagnosticCampaignTravel(int Level, bool TransitionActive);

/// <summary>Copied screen/focus/text facts and the ordinary menu's bounded user actions. No node tree escapes.</summary>
public interface IDiagnosticMenuFixture
{
    DiagnosticNavigationSnapshot ReadNavigation();
    DiagnosticStartupPresentation ReadStartupPresentation();
    ImmutableArray<string> ReadVisibleText(DiagnosticMenuSurface surface);
    void ShowTitle();
    void ShowWorldSelect();
    bool Back();
    void PressTitleStart();
    void PressLab();
    bool OpenPause();
    bool ClosePause();
    void ShowPauseSection(string section);
}

public interface IDiagnosticPlayerContinuity { DiagnosticPlayerContinuity Capture(); }
public interface IDiagnosticProfileEvidence
{
    bool HasExplicitPath { get; }
    string? ReadDigest();
    bool TrySave(string reason);
}
public interface IDiagnosticSourceRepairFixture
{
    bool Detach(ObpSourceGame game);
    bool RequireReady(string reason);
    DiagnosticCampaignTravel ReadCampaignTravel();
    bool StageCampaignTravel(int destination);
}

public static class OrdinaryUiSurfacePolicy
{
    public static void Verify(IEnumerable<string> visibleText)
    {
        foreach (string text in visibleText)
            if (Regex.IsMatch(text, @"game sources|source (needed|ready|connected|unavailable)|attach|\.iso\b|rac[123]-ntsc|shared profile|provider|adapter|build id|native destination",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                throw new InvalidOperationException("Setup/provenance leaked into ordinary UI: " + text);
    }
}

public sealed record DiagnosticRect(float X, float Y, float Width, float Height);
public sealed record DiagnosticRetailPanel(string Id, bool Visible, bool RetailPauseRoot, int SurfaceCount, DiagnosticRect Bounds);
public sealed record DiagnosticPanelPixel(string Id, DiagnosticRect Bounds, int SampleX, int SampleY, float R, float G, float B);
public sealed record DiagnosticPanelCapture(int Width, int Height, ImmutableArray<DiagnosticPanelPixel> Panels);
public sealed record DiagnosticRetailPauseSnapshot(bool AssetsReady, bool Rac1World, bool PlayerPresent,
    bool PauseOpen, bool TreePaused, bool RetailEnabled, Vector3 PlayerPosition, int HeldItem,
    ImmutableArray<int> QuickSelect, ImmutableArray<int> Ammo, ImmutableArray<DiagnosticRetailPanel> Panels,
    string? FocusId, string? FocusText, ImmutableArray<string> ItemCells, Rac1RetailMenuPage? Page,
    string PointerMode, bool ControllerWasLastInput, bool Headless, float ViewportWidth, float ViewportHeight);

/// <summary>One live pause fixture. Input admission and temporary viewport/pointer state belong to its disposable review lease.</summary>
public interface IDiagnosticRetailPauseFixture
{
    DiagnosticRetailPauseSnapshot Read();
    bool Open();
    bool Close();
    IDisposable BeginInputReview();
    void InjectAction(string action, bool pressed, bool admitted);
    DiagnosticPanelCapture CapturePanels(string folder, string fileName);
}
