using Godot;
using OBP.Core;

namespace OneBigPackage;

/// <summary>
/// Small scene-level adapter that layers trilogy source ownership around the
/// existing application root. It remembers direct trilogy source args and
/// intercepts Escape early enough to provide the neutral navigation stack:
/// world → Worlds → Game Sources.
/// </summary>
public partial class SourceManagerBootstrap : Node
{
    private OBPGame? _game;

    public override void _Ready()
    {
        // The composition / fusion lab replaces the whole OBPGame host tree, and
        // a --shots run drives its own world entry — neither wants the trilogy
        // source-manager navigation stack.
        if (System.Array.Exists(OS.GetCmdlineUserArgs(), a => a is "--compose" or "--composition" or "--shots"))
        {
            return;
        }

        _game = GetParentOrNull<OBPGame>();
        CallDeferred(nameof(Activate));
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }
            || _game is null)
        {
            return;
        }

        if (_game.TryReturnWorldToDestinations() || _game.TryReturnSelectorToSources())
        {
            GetViewport().SetInputAsHandled();
        }
    }

    private void Activate()
    {
        if (_game is null)
        {
            return;
        }

        string[] args = OS.GetCmdlineUserArgs();
        string? rac1Path = ValueAfter(args, "--rac1-iso");
        string? gcPath = ValueAfter(args, "--gc-iso");
        string? uyaPath = ValueAfter(args, "--uya-iso");
        string? destinationId = ValueAfter(args, "--destination");
        string? testScene = ValueAfter(args, "--test-scene");

        if (rac1Path is not null)
        {
            _game.RememberCommandLineSource(ObpSourceGame.Rac1, rac1Path);
        }
        if (gcPath is not null)
        {
            _game.RememberCommandLineSource(ObpSourceGame.Rac2, gcPath);
        }
        if (uyaPath is not null)
        {
            _game.RememberCommandLineSource(ObpSourceGame.Rac3, uyaPath);
        }

        if (destinationId is not null)
        {
            _game.OpenDestinationFromBootstrap(destinationId);
            return;
        }

        if (testScene == "worlds")
        {
            _game.ShowDestinationSelectorFromBootstrap();
            return;
        }

        // Interactive executable/editor launch with no explicit test scene now
        // lands on the trilogy source manager. Deterministic harnesses always
        // pass --test-scene and therefore retain their existing smoke/player
        // behaviour. --test-scene picker is the deterministic source-screen path.
        if (testScene == "picker" || (testScene is null && gcPath is null))
        {
            _game.ShowSourceManagerFromBootstrap();
        }
    }

    private static string? ValueAfter(string[] args, string option)
    {
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == option)
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
