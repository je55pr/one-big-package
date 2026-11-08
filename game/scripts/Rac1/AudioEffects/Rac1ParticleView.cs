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
    public ObpSourceRef SourceBank { get; set; } = Bank;
    private EffectSceneView? _view;
    private IReadOnlyDictionary<int, RuntimeTexture>? _boundTextures;
    private int _boundCount;
    private ObpSourceRef? _boundBank;
    private RuntimeEffectTexture[]? _bindings;
    public int RenderedCount => _view?.RenderedCount ?? 0;
    public int DrawCalls => _view?.DrawCalls ?? 0;
#pragma warning disable S2360 // Null preserves every existing native donor caller; recipients opt in to output-only palette conversion.
    public void Render(Rac1ParticleSession session, IReadOnlyDictionary<int, RuntimeTexture> textures, Func<uint, uint>? palette = null)
#pragma warning restore S2360
    {
        if (session.Count == 0) { if (_view?.TextureCount > 0) _view.Clear(); return; }
        _view ??= new(this);
        // The bindings depend only on the texture set and bank, which callers keep for the whole level.
        if (!ReferenceEquals(_boundTextures, textures) || _boundCount != textures.Count || _boundBank != SourceBank)
        {
            _bindings = textures.Values.Select(t => new RuntimeEffectTexture(SourceBank with
            { AssetKind = t.AssetKind, OriginalId = t.TextureId.ToString(System.Globalization.CultureInfo.InvariantCulture) }, t)).ToArray();
            (_boundTextures, _boundCount, _boundBank) = (textures, textures.Count, SourceBank);
        }
        var bindings = _bindings!;
        _view.Render([Rac1ParticlePresentation.Capture(session, SourceBank, textures, palette: palette)], bindings);
    }
    public override void _ExitTree() { _view?.Dispose(); _view = null; }
}
