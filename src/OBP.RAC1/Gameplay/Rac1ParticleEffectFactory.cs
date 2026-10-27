using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using OBP.Core;
using OBP.IO;
using OBP.RAC1.Level;
using OBP.Runtime.Effects;
using OBP.Runtime.Presentation;

namespace OBP.RAC1.Gameplay;

/// <summary>Neutral port around the recovered Novalis class-27 caller; does not replace its simulation.</summary>
public sealed class Rac1ParticleEffectFactory : IRuntimeEffectFactory
{
    public const string Recipe = "rac1/class27/type6/v1";
    private readonly Rac1EffectAssets _assets;
    private readonly Rac1ParticleEmitter _template;
    public RuntimeEffectRecipeKey Key { get; }
    public int NativeTicksPerSecond => 60;
    public IReadOnlyList<RuntimeEffectTexture> Textures { get; }
    public Rac1ParticleEffectFactory(Rac1EffectAssets assets, Rac1ParticleEmitter template)
    {
        _assets = assets; _template = template; Key = new(template.Source, Recipe);
        Textures = assets.PartTextures.Values.Select(t => new RuntimeEffectTexture(template.Source with
        { AssetKind = t.AssetKind, OriginalId = t.TextureId.ToString(CultureInfo.InvariantCulture) }, t)).ToArray();
    }
    public static IReadOnlyList<Rac1ParticleEffectFactory> ReadNovalis(IRandomAccessReader disc)
    {
        var level = Rac1DiscIndex.Read(disc).Levels.Single(l => l.LevelId == 1);
        var templates = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(disc, level)).MobyInstances
            .Where(m => m.OClass == 27).Select(m => Rac1ParticleEmitter.FromAuthored(1, m)).ToArray();
        var assets = Rac1EffectAssets.DecodeEmitters(Rac1LevelCore.Open(disc, level), templates.Select(t => t.Parameters));
        return templates.Select(t => new Rac1ParticleEffectFactory(assets, t)).ToArray();
    }
    public IRuntimeEffectInstance Create(uint seed, RuntimeEffectPose pose)
    {
        var parameters = _template.Parameters;
        if (parameters.BurstCount != 0 && (!_assets.ParticleChoices.TryGetValue(parameters.TextureDefinition, out var choices) ||
            choices.Length == 0 || choices.Any(texture => !_assets.PartTextures.ContainsKey(texture))))
            throw new InvalidDataException("The class-27 recipe requires its original Part texture definition and images.");
        return new Instance(this, seed, pose);
    }
    private sealed class Instance : IRuntimeEffectInstance
    {
        private readonly Rac1ParticleEffectFactory _factory;
        private readonly Rac1ParticleSession _session;
        private readonly Rac1ParticleEmitter _owner;
        private readonly Vector3 _placement;
        private Rac1ParticleEmitterView? _previousView;
        private long _tick, _attempted, _emitted, _randomSamples;
        public Instance(Rac1ParticleEffectFactory factory, uint seed, RuntimeEffectPose pose)
        {
            pose.Validate(); _factory = factory;
            var random = Rac1PickupRandom.FromSeed(seed);
            _session = new(factory._assets, new(() => { _randomSamples++; return random.Next15(); }));
            var template = factory._template;
            _placement = pose.Position - new Vector3(template.Position.X, template.Position.Z, template.Position.Y);
            _owner = new(template.Source, template.Owner, template.Parameters, template.Position, template.Rotation);
        }
        public RuntimeEffectState State => new(_tick, _session.Count, _owner.EmissionEnabled, _attempted, _emitted, _randomSamples,
            "Godot sprite presentation retains the existing bounded depth/order coverage; full GS rasterization is not emulated.");
        public void Step(RuntimeEffectView view)
        {
            var emission = _session.Emit(_owner, _previousView); _attempted += emission.Requested; _emitted += emission.Created;
            _session.Step(Native(view.Eye - _placement)); _tick++;
            _previousView = new(Native(view.Eye - _placement), Native(view.Forward), Native(view.Right), Native(view.Up), view.TanX, view.TanY);
        }
        public void SetPose(RuntimeEffectPose pose) { pose.Validate(); _owner.ApplyNativeFrame(_owner.Parameters, Native(pose.Position - _placement), _owner.Rotation); }
        public void StopEmission() => _owner.StopEmission();
        public void RetireOwner() => _owner.Retire(); // Type 6 really retains its native owner; it cannot drain a dead owner's parameters.
        public RuntimeEffectFrame Capture(RuntimeEffectView view) => Rac1ParticlePresentation.Capture(_session, _factory.Key.Source, _factory._assets.PartTextures, _placement);
        public void Dispose() { _owner.Retire(); _session.Clear(); }
    }
    internal static Vector3 Native(Vector3 value) => new(value.X, value.Z, value.Y);
}

/// <summary>Presentation interpretation for the existing R&C1 pool, shared by its weapon and class-27 hosts.</summary>
public static class Rac1ParticlePresentation
{
    public static RuntimeEffectFrame Capture(Rac1ParticleSession session, ObpSourceRef bank,
        IReadOnlyDictionary<int, OBP.Runtime.RuntimeTexture> textures, Vector3 placement = default)
    {
        var sprites = ImmutableArray.CreateBuilder<RuntimeEffectSprite>();
        foreach (var p in session.Live)
        {
            if (p.Size <= 0 || p.Remaining <= 0) continue;
            if (!textures.TryGetValue(p.Texture, out var texture)) throw new InvalidDataException("R&C1 particle texture is unavailable.");
            var source = bank with { AssetKind = texture.AssetKind, OriginalId = p.Texture.ToString(CultureInfo.InvariantCulture) };
            var color = new Vector4((p.Color & 255) / 128f, (p.Color >> 8 & 255) / 128f, (p.Color >> 16 & 255) / 128f, (p.Color >> 24) / 128f);
            var draw = new RuntimeEffectDrawState(p.Blending == Rac1ParticleSession.Blend.Additive ? RuntimeBlendEquation.AdditiveSourceAlpha : RuntimeBlendEquation.SourceAlpha,
                NearFade: (p.DepthRange & 15) * .25f, FarFade: (p.DepthRange & 0xf0) * 2f, DepthFade: true);
            sprites.Add(new(p.Identity, p.Slot, source, new Vector3(p.Position.X, p.Position.Z, p.Position.Y) + placement, p.Size, color, p.Rotation, draw));
        }
        return new(sprites.ToImmutable(), [], []);
    }
}
