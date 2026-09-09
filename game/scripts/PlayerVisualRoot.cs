using Godot;

namespace OneBigPackage;

/// <summary>
/// Presentation-only child of <see cref="DebugPlayer"/>. The controller and
/// collision remain on the CharacterBody3D; this node owns whichever visible
/// avatar is currently attached.
/// </summary>
public partial class PlayerVisualRoot : Node3D
{
    private MeshInstance3D? _fallbackVisual;
    private Node3D? _customVisual;

    /// <summary>The currently attached replacement visual, or null while using the fallback.</summary>
    public Node3D? CustomVisual => _customVisual;

    /// <summary>Whether the built-in debug capsule is currently visible.</summary>
    public bool FallbackVisible
    {
        get => _fallbackVisual?.Visible ?? false;
        set
        {
            if (_fallbackVisual is not null)
            {
                _fallbackVisual.Visible = value;
            }
        }
    }

    /// <summary>Create the legacy yellow debug capsule as a replaceable fallback visual.</summary>
    public void ConfigureDebugFallback(float radius, float height)
    {
        if (_fallbackVisual is not null)
        {
            return;
        }

        _fallbackVisual = new MeshInstance3D
        {
            Name = "Body",
            Mesh = new CapsuleMesh { Radius = radius, Height = height },
            Position = new Vector3(0, height / 2f, 0),
            // Preserve the existing lit debug-player look so the host's GC hero
            // directional light still affects the fallback capsule.
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.74f, 0.15f),
                Roughness = 0.7f,
            },
        };
        AddChild(_fallbackVisual);
    }

    /// <summary>
    /// Replace the presentation node without changing the controller body. Passing
    /// null removes the custom visual and restores the debug fallback.
    /// </summary>
    public void ReplaceVisual(Node3D? visual)
    {
        if (ReferenceEquals(_customVisual, visual))
        {
            return;
        }

        if (_customVisual is not null && GodotObject.IsInstanceValid(_customVisual))
        {
            if (_customVisual.GetParent() == this)
            {
                RemoveChild(_customVisual);
            }
            _customVisual.QueueFree();
        }

        _customVisual = visual;
        if (_customVisual is null)
        {
            FallbackVisible = true;
            return;
        }

        if (_customVisual.GetParent() is null)
        {
            AddChild(_customVisual);
        }
        else
        {
            _customVisual.Reparent(this, keepGlobalTransform: false);
        }

        FallbackVisible = false;
    }
}
