using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using OBP.IO;
using OBP.RAC3.Level;
using OBP.Runtime.Effects;
using OBP.Runtime.Presentation;

namespace OBP.RAC3.Gameplay;

/// <summary>The two independently recovered TABLE1 Cannon callers, as visual generators without weapon/damage execution.</summary>
public sealed class Rac3ParticleEffectFactory : IRuntimeEffectFactory
{
    public const string ChargingRecipe = "rac3/shock-cannon/charging/v1";
    public const string BeamGlowRecipe = "rac3/shock-cannon/beam-glow/v1";
    private readonly Rac3ParticleAssets _assets;
    private readonly Rac3ParticleDraw _draw;
    private readonly bool _beam;
    private readonly int _chargeAge;
    public RuntimeEffectRecipeKey Key { get; }
    public int NativeTicksPerSecond => 60;
    public int? MaximumEmissionTicks => _beam ? null : 79 - _chargeAge;
    public IReadOnlyList<RuntimeEffectTexture> Textures { get; }
    public Rac3ParticleEffectFactory(Rac3ParticleAssets assets, Rac3ParticleDraw draw, bool beam, int nativeChargeAge = 1)
    {
        if (nativeChargeAge is < 1 or > 78) throw new NotSupportedException("Only the recovered pre-ready charge caller is registered.");
        _assets = assets; _draw = draw; _beam = beam; _chargeAge = nativeChargeAge;
        Key = new(assets.Source with { AssetKind = "particle-caller", OriginalId = beam ? "38667c+386724" : "385f18" }, beam ? BeamGlowRecipe : ChargingRecipe);
        Textures = assets.Textures.Values.Select(t => new RuntimeEffectTexture(assets.Source with
        { AssetKind = t.AssetKind, OriginalId = t.TextureId.ToString(CultureInfo.InvariantCulture) }, t)).ToArray();
    }
    public static IReadOnlyList<Rac3ParticleEffectFactory> ReadVeldin(IRandomAccessReader disc)
    {
        var level = Rac3LevelCore.Open(disc, 1);
        var assets = Rac3ParticleAssets.Read(level.Core, new(OBP.Core.ObpSourceGame.Rac3, Rac3Authority.Primary.BuildId, "TABLE1"));
        var draw = Rac3ParticleDraw.ReadVeldin(level);
        return [new(assets, draw, false), new(assets, draw, true)];
    }
    public IRuntimeEffectInstance Create(uint seed, RuntimeEffectPose pose) => new Instance(this, seed, pose);
    private sealed class Instance : IRuntimeEffectInstance
    {
        private readonly Rac3ParticleEffectFactory _factory;
        private readonly Rac3ParticleSession _session;
        private readonly Rac3ParticleRandom _random;
        private readonly Rac3ParticleOwner _owner;
        private RuntimeEffectPose _pose;
        private long _tick, _attempted, _emitted;
        private int _chargeAge;
        private bool _stopped;
        public Instance(Rac3ParticleEffectFactory factory, uint seed, RuntimeEffectPose pose)
        {
            pose.Validate(); _factory = factory; _pose = pose; _chargeAge = factory._chargeAge;
            _random = Rac3ParticleRandom.FromSeed(seed); _session = new(factory._assets, _random); _owner = new(factory.Key.Source, 0);
            _session.RequireCannonResources(_owner);
        }
        public RuntimeEffectState State => new(_tick, _session.Count, !_stopped, _attempted, _emitted, _random.SamplesConsumed,
            "Static-world type 97 only; ready-charge composite, native full sorting and VU1/GS rasterization remain unsupported.");
        public void Step(RuntimeEffectView view)
        {
            _session.Step();
            if (!_stopped)
            {
                var emission = _factory._beam
                    ? _session.EmitShockCannonBeamGlow(_owner, new(Native(_pose.Position), 1), new(Native(_pose.End), 1))
                    : _session.EmitShockCannonCharging(_owner, _chargeAge++, new(Native(_pose.Position), 1));
                _attempted += emission.Attempted; _emitted += emission.Emitted;
                if (!_factory._beam && _chargeAge == 79) _stopped = true;
            }
            _tick++;
        }
        public void SetPose(RuntimeEffectPose pose) { pose.Validate(); _pose = pose; }
        public void StopEmission() => _stopped = true;
        public void RetireOwner() => _stopped = true; // Type 97 retains copied positions/velocities rather than a held-Moby pointer.
        public RuntimeEffectFrame Capture(RuntimeEffectView view)
        {
            var sprites = ImmutableArray.CreateBuilder<RuntimeEffectSprite>();
            var nativeView = new Rac3ParticleDraw.View(Native(view.Eye), Native(view.Forward), view.ProjectionY / 420000, int.MaxValue);
            foreach (var p in _session.Live)
                if (_factory._draw.Project(p, nativeView) is { } projection)
                {
                    uint c = projection.Color;
                    var color = new Vector4((c & 255) / 128f, (c >> 8 & 255) / 128f, (c >> 16 & 255) / 128f, (c >> 24) / 128f);
                    sprites.Add(new(p.Identity, p.Slot, p.TextureSource, new(p.Position.X, p.Position.Z, p.Position.Y), 1, color, 0,
                        new(RuntimeBlendEquation.AdditiveSourceAlpha), RuntimeEffectSpriteProjection.ProjectedBillboard, projection.Axes));
                }
            return new(sprites.ToImmutable(), [], []);
        }
        public void Dispose() { _stopped = true; _session.Clear(); }
    }
    private static Vector3 Native(Vector3 value) => new(value.X, value.Z, value.Y);
}
