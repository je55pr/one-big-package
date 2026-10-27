using Godot;
using OBP.Godot;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>Existing weapon/beam composition over the shared neutral mesh presentation.</summary>
public sealed partial class FusionMeshView : Node3D
{
    private EffectMeshSceneView? _view;
    public void Sample(IReadOnlyList<RuntimeObjectMesh> surfaces, FusionWeaponAssets assets)
    {
        _view ??= new(this); _view.Sample(surfaces, assets.MaterialFor);
    }
    public override void _ExitTree() { _view?.Dispose(); _view = null; }
}
