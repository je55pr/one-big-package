using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OBP.Core;
using OBP.RAC1.Progression;
using OBP.Runtime.Player;

namespace OBP.Game.Diagnostics;

internal sealed class GodotDiagnosticMenus(Node owner, Node3D worldRoot,
    Func<DiagnosticNavigationSnapshot> navigation, Func<DiagnosticStartupPresentation> presentation,
    Func<DiagnosticMenuSurface, Node?> surface, Func<TitleScreen?> title,
    Func<DestinationSelectorUi?> selector, Func<PauseMenuUi?> pause,
    Action showTitle, Action showWorldSelect, Func<bool> back, Func<bool> openPause, Func<bool> closePause) : IDiagnosticMenuFixture
{
    public DiagnosticNavigationSnapshot ReadNavigation() => navigation() with
    {
        SaveNotice = title()?.FindChild("SaveNotice", true, false) is Label { Visible: true } notice ? notice.Text : null,
        LabButtonPresent = selector()?.FindChild("FusionLab", true, false) is Button,
        EnabledButtonFocused = owner.GetViewport().GuiGetFocusOwner() is Button { Disabled: false },
    };
    public DiagnosticStartupPresentation ReadStartupPresentation() => presentation() with
    {
        VisiblePlaceholders = [.. new[] { "SmokeCube", "Floor" }.Where(name =>
            worldRoot.GetNodeOrNull<MeshInstance3D>(name)?.IsVisibleInTree() == true)],
    };
    public ImmutableArray<string> ReadVisibleText(DiagnosticMenuSurface target) =>
        GodotDiagnosticSurface.Read(surface(target) ?? throw new DiagnosticUnavailableException("The requested UI surface is missing: " + target));
    public void ShowTitle() => showTitle();
    public void ShowWorldSelect() => showWorldSelect();
    public bool Back() => back();
    public void PressTitleStart()
    {
        using var press = new InputEventJoypadButton { ButtonIndex = JoyButton.Start, Pressed = true };
        (title() ?? throw new DiagnosticUnavailableException("The title is missing."))._UnhandledInput(press);
    }
    public void PressLab() => (selector()?.FindChild("FusionLab", true, false) as Button
        ?? throw new DiagnosticUnavailableException("The Lab button is missing.")).EmitSignal(Button.SignalName.Pressed);
    public bool OpenPause() => openPause();
    public bool ClosePause() => closePause();
    public void ShowPauseSection(string section) => (pause() ?? throw new DiagnosticUnavailableException("The pause menu is missing.")).ShowUniversalSection(section);
}

internal static class GodotDiagnosticSurface
{
    public static ImmutableArray<string> Read(Node node)
    {
        var text = ImmutableArray.CreateBuilder<string>();
        Visit(node, text); return text.ToImmutable();
    }
    private static void Visit(Node node, ImmutableArray<string>.Builder result)
    {
        if (node.IsQueuedForDeletion() || node is CanvasItem item && !item.IsVisibleInTree() ||
            node is CanvasLayer layer && !layer.Visible || node is Node3D spatial && !spatial.IsVisibleInTree()) return;
        string text = node switch { Label label => label.Text, Button button => button.Text, RichTextLabel rich => rich.Text, Label3D sign => sign.Text, _ => "" };
        if (node is Control control) text += "\n" + control.TooltipText;
        result.Add(text);
        foreach (Node child in node.GetChildren()) Visit(child, result);
    }
}

internal sealed class DiagnosticPlayerContinuityAdapter(Func<PlayerSessionSnapshot> capture) : IDiagnosticPlayerContinuity
{
    public DiagnosticPlayerContinuity Capture()
    {
        var state = capture();
        return new(state.Configuration, state.Health, JsonSerializer.Serialize(state.Loadout), JsonSerializer.Serialize(state.Currencies));
    }
}

internal sealed class DiagnosticProfileEvidence(Func<string?> path, Func<string, bool> save) : IDiagnosticProfileEvidence
{
    public bool HasExplicitPath => path() is not null;
    public string? ReadDigest()
    {
        if (path() is not { } file || !File.Exists(file)) return null;
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    public bool TrySave(string reason) => save(reason);
}

internal sealed class DiagnosticSourceRepairFixture(Func<ObpSourceGame, bool> detach, Func<string, bool> requireReady,
    Func<Rac1CampaignRuntimeSession> campaign) : IDiagnosticSourceRepairFixture
{
    public bool Detach(ObpSourceGame game) => detach(game);
    public bool RequireReady(string reason) => requireReady(reason);
    public DiagnosticCampaignTravel ReadCampaignTravel() => new(campaign().Campaign.CurrentLevel, campaign().Travel.TransitionActive);
    public bool StageCampaignTravel(int destination)
    {
        var session = campaign();
        session.Campaign.AdmitDestination(destination);
        return session.BeginTravel(destination) == Rac1PlanetTravelStartResult.Started;
    }
}

internal sealed class DiagnosticNavigationActions(Func<string, bool> navigate) : IDiagnosticNavigationActions
{
    public Task<DiagnosticActionResult> NavigateAsync(string destinationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DiagnosticActionResult(navigate(destinationId), null));
    }
}
