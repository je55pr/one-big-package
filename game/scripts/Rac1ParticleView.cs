using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.RAC1;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Effects;

namespace OBP.Game;

/// <summary>Compatibility composition for the source-owned weapon pool, using the shared qualified sprite renderer.</summary>
public sealed partial class Rac1ParticleView : Node3D
{
    private static readonly ObpSourceRef Bank = new(ObpSourceGame.Rac1, Rac1Authority.Primary.BuildId, "LEVEL0");
    private EffectSceneView? _view;
    public int RenderedCount => _view?.RenderedCount ?? 0;
    public int DrawCalls => _view?.DrawCalls ?? 0;
    public void Render(Rac1ParticleSession session, IReadOnlyDictionary<int, RuntimeTexture> textures)
    {
        _view ??= new(this);
        var bindings = textures.Values.Select(t => new RuntimeEffectTexture(Bank with
        { AssetKind = t.AssetKind, OriginalId = t.TextureId.ToString(System.Globalization.CultureInfo.InvariantCulture) }, t)).ToArray();
        _view.Render([Rac1ParticlePresentation.Capture(session, Bank, textures)], bindings);
    }
    public override void _ExitTree() { _view?.Dispose(); _view = null; }
}
