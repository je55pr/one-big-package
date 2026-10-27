using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.IO;
using OBP.PS2;

namespace OBP.Game;

internal sealed class GodotSourceSetupConsumers(Node owner, Action<ObpSourceGame> invalidate, Action<bool> prepare) : ISourceSetupConsumers
{
    public void Invalidate(ObpSourceGame game) => invalidate(game);
    public void Prepare(bool force) => prepare(force);
    public string? RefreshPresentation(ObpAttachedSource? source, IRandomAccessReader? reader)
    {
        if (source is not null && source.Game != ObpSourceGame.Rac1) return null;
        string? warning = null;
        try
        {
            if (source is null) Rac1RetailUiAssets.Clear();
            else Rac1RetailUiAssets.Load(reader ?? throw new InvalidOperationException("UI source has no disc reader."));
        }
        catch (Exception error)
        {
            Rac1RetailUiAssets.Clear(); warning = "Could not read R&C1 UI assets: " + error.Message; GD.PushWarning(warning);
        }
        UiTheme.Apply(owner.GetTree().Root);
        return warning;
    }
}
