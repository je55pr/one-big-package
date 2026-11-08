using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.IO;
using OBP.PS2;

namespace OBP.Game;

internal sealed class GodotSourceSetupConsumers(Node owner, Action<ObpSourceGame> invalidate, Action<bool> prepare) : ISourceSetupConsumers
{
    public void Invalidate(ObpSourceGame game)
    {
        WeaponMenuIcons.Clear(game);
        invalidate(game);
    }
    public void Prepare(bool force) => prepare(force);
    public string? RefreshPresentation(ObpAttachedSource? source, IRandomAccessReader? reader)
    {
        try
        {
            if (source is null)
            {
                WeaponMenuIcons.Clear(ObpSourceGame.Rac1);
                Rac1RetailUiAssets.Clear();
            }
            else
            {
                if (source.Game == ObpSourceGame.Rac1)
                    Rac1RetailUiAssets.Load(reader ?? throw new InvalidOperationException("UI source has no disc reader."));
                WeaponMenuIcons.Load(source.Game, reader);
            }
            if (source is null || source.Game == ObpSourceGame.Rac1) UiTheme.Apply(owner.GetTree().Root);
            return null;
        }
        catch (Exception error)
        {
            if (source is not null) WeaponMenuIcons.Clear(source.Game);
            if (source is null || source.Game == ObpSourceGame.Rac1) Rac1RetailUiAssets.Clear();
            string problem = "Required source UI assets could not be read: " + error.Message;
            GD.PrintErr(problem);
            ApplicationLifecycle.RequestQuit(owner, "required-source-ui-unavailable", 2);
            return problem;
        }
    }
}
