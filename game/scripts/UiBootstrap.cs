using Godot;

namespace OneBigPackage;

/// <summary>
/// Autoload that installs the application-wide <see cref="UiTheme"/> on the root
/// window so every screen shares one look. Runs before the main scene; does
/// nothing else.
/// </summary>
public partial class UiBootstrap : Node
{
    public override void _Ready()
    {
        GetTree().Root.Theme = UiTheme.Build();

        // The engine appends "(DEBUG)" to the window title for non-exported
        // builds; the showcase launcher is one, so pin a clean title.
        GetWindow().Title = "One Big Package";
    }
}
