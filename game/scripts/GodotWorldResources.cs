using Godot;
using OBP.Godot;
using OBP.Runtime;
using OBP.Fusion;

namespace OBP.Game;

/// <summary>Godot resources for one prepared or adopted scene, including a bound transient player node.</summary>
internal sealed class GodotWorldResources(RuntimeWorldScene.Result prepared) : IDisposable
{
    private PlayerHost? _player;
    private bool _disposed;
    public WorldHost Presentation { get; } = new();
    public RuntimeWorldScene.Result Scene { get; } = prepared;
    public WorldEffectHost? Effects { get; set; }
    public WorldEntryRequest? EffectScope { get; set; }

    public void Load(Node owner, Node3D parent, RuntimeWorld world, string name, WorldHost.Options options) =>
        Presentation.Load(owner, parent, world, name, options, Scene);

    public void BindPlayer(PlayerHost? player) => _player = player;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            Effects?.Dispose(); Effects = null;
            if (GodotObject.IsInstanceValid(_player))
            {
                if (_player!.GetParent() is { } parent) parent.RemoveChild(_player);
                if (!_player.IsQueuedForDeletion()) _player.QueueFree();
            }
        }
        finally
        {
            // Load can fail after allocating the environment but before attaching
            // the staged root. Both parts must be retired even on partial load.
            bool ownedByPresentation = Presentation.Result is not null;
            Presentation.Unload();
            if (!ownedByPresentation && GodotObject.IsInstanceValid(Scene.Root) && !Scene.Root.IsQueuedForDeletion())
            {
                if (Scene.Root.IsInsideTree()) Scene.Root.QueueFree();
                else Scene.Root.Free();
            }
        }
    }
}
