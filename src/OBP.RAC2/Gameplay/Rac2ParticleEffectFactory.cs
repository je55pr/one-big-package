using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using OBP.IO;
using OBP.PS2.Iso;
using OBP.RAC2.Level;
using OBP.Runtime.Effects;
using OBP.Runtime.Presentation;

namespace OBP.RAC2.Gameplay;

/// <summary>Registered Museum caller and its original parameter block, independent of the destination world.</summary>
public sealed class Rac2ParticleEffectFactory : IRuntimeEffectFactory
{
    public const string Recipe = "rac2/museum/type85/v1";
    // OBP presentation conversion: native editor diameter units to NDC half extent.
    // This is not a recovered GS camera-global value; native Project still owns depth/rotation/fade.
    private const float PresentationProjectionUnits = 2 * Rac2ParticleSession.EditorSizeUnits;
    private readonly Rac2ParticleAssets _assets;
    private readonly Rac2MuseumParticleEmitter _template;
    private readonly Rac2ParticleDraw _draw;
    public RuntimeEffectRecipeKey Key { get; }
    public int NativeTicksPerSecond => 60;
    public IReadOnlyList<RuntimeEffectTexture> Textures { get; }
    public Rac2ParticleEffectFactory(Rac2ParticleAssets assets, Rac2MuseumParticleEmitter template, Rac2ParticleDraw draw)
    {
        _assets = assets; _template = template; _draw = draw; Key = new(template.Source, Recipe);
        Textures = assets.Textures.Values.Select(t => new RuntimeEffectTexture(template.Source with
        { AssetKind = t.AssetKind, OriginalId = t.TextureId.ToString(CultureInfo.InvariantCulture) }, t)).ToArray();
    }
    public static IReadOnlyList<Rac2ParticleEffectFactory> ReadMuseum(IRandomAccessReader disc)
    {
        var wad = Iso9660Filesystem.Open(disc).OpenFile("/G/LEVEL21.WAD") ?? throw new InvalidDataException("GC Museum is unavailable.");
        var header = Rac2LevelWad.ReadHeader(wad);
        var data = Rac2LevelWad.RequireLump(wad, header, 0);
        var bank = new OBP.Core.ObpSourceRef(OBP.Core.ObpSourceGame.Rac2, Rac2Authority.Primary.BuildId, "LEVEL21");
        var assets = Rac2ParticleAssets.Read(Rac2LevelCore.Open(data), bank);
        var draw = Rac2ParticleDraw.ReadMuseum(Rac2LevelOverlay.Open(data));
        return Rac2Instances.Read(Rac2LevelWad.RequireLump(wad, header, 2)).MobyInstances
            .Where(m => m.OClass == Rac2MuseumParticleEmitter.NativeClassId)
            .Select(m => new Rac2ParticleEffectFactory(assets, Rac2MuseumParticleEmitter.FromAuthored(bank, m), draw)).ToArray();
    }
    public IRuntimeEffectInstance Create(uint seed, RuntimeEffectPose pose)
    {
        var source = _template.Source;
        if (source.Game != _assets.Source.Game || source.BuildId != _assets.Source.BuildId || source.LevelId != _assets.Source.LevelId ||
            !_assets.Textures.ContainsKey(_assets.Definitions.Texture(85, 0)) ||
            !_assets.Textures.ContainsKey(_assets.Definitions.Texture(_template.Parameters.TextureDefinition, _template.Parameters.TextureFrame)))
            throw new InvalidDataException("The Museum recipe requires its original source bank, constructor texture and authored image.");
        return new Instance(this, seed, pose);
    }
    private sealed class Instance : IRuntimeEffectInstance
    {
        private readonly Rac2ParticleEffectFactory _factory;
        private readonly Rac2ParticleSession _session;
        private readonly Rac2ParticleRandom _random;
        private readonly Rac2MuseumParticleEmitter _owner;
        private long _tick, _attempted, _emitted;
        public Instance(Rac2ParticleEffectFactory factory, uint seed, RuntimeEffectPose pose)
        {
            pose.Validate(); _factory = factory; _random = Rac2ParticleRandom.FromSeed(seed);
            _session = new(factory._assets, _random);
            _owner = new(factory._template.Source, factory._template.Parameters, Native(pose.Position));
        }
        public RuntimeEffectState State => new(_tick, _session.Count, _owner.EmissionEnabled, _attempted, _emitted, _random.SamplesConsumed,
            "Measured sprite projection; Godot does not emulate full native bucket sorting, conditional depth writes or VU1/GS rasterization.");
        public void Step(RuntimeEffectView view)
        {
            _session.Step();
            var emission = _session.Emit(_owner);
            if (emission.Status is Rac2ParticleSession.EmissionStatus.Emitted or Rac2ParticleSession.EmissionStatus.PoolFull) _attempted++;
            if (emission.Particle is not null) _emitted++;
            _tick++;
        }
        public void SetPose(RuntimeEffectPose pose) { pose.Validate(); _owner.ApplyNativeFrame(_owner.Parameters, Native(pose.Position)); }
        public void StopEmission() => _owner.StopEmission();
        public void RetireOwner() => _owner.StopEmission(); // Type 85 copies its inputs; existing particles retain no live owner pointer.
        public RuntimeEffectFrame Capture(RuntimeEffectView view)
        {
            var sprites = ImmutableArray.CreateBuilder<RuntimeEffectSprite>();
            var nativeView = new Rac2ParticleDraw.View(Native(view.Eye), Native(view.Forward), view.ProjectionY / PresentationProjectionUnits, int.MaxValue);
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
        public void Dispose() { _owner.StopEmission(); _session.Clear(); }
    }
    private static Vector3 Native(Vector3 value) => new(value.X, value.Z, value.Y);
}
