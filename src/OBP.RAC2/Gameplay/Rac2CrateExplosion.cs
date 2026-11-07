using System.Globalization;
using System.Collections.Immutable;
using System.Numerics;
using OBP.Core;
using OBP.IO;
using OBP.PS2.Iso;
using OBP.RAC2.Level;
using OBP.Runtime.Effects;
using OBP.Runtime.Presentation;
using M = OBP.RAC2.Gameplay.Rac2ParticleMath;

namespace OBP.RAC2.Gameplay;

/// <summary>
/// Class-505's scale-one, 10/10/10/10/15 explosion recipe, loaded Oozla
/// 0x392778..0x3927B4 and 0x3F8F68..0x3F9BC4. The host supplies camera/player
/// observations; native particle motion and active-level assets remain source-owned.
/// </summary>
public sealed class Rac2CrateExplosion
{
    private readonly Rac2ParticleAssets _assets;
    private readonly Rac2ParticleSession _particles;
    private readonly Rac2ParticleRandom _random;
    private readonly Rac2ExplosionTrails _trails;
    public IReadOnlyList<RuntimeEffectTexture> Textures { get; }
    public int Count => _particles.Count + _trails.Count;

    public Rac2CrateExplosion(string path, int level, uint seed)
    {
        using var reader = new FileRandomAccessReader(path);
        var wad = Iso9660Filesystem.Open(reader).OpenFile($"/G/LEVEL{level}.WAD")
            ?? throw new InvalidDataException("Explosion source level is unavailable.");
        var header = Rac2LevelWad.ReadHeader(wad);
        _assets = Rac2ParticleAssets.Read(Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0)),
            new(ObpSourceGame.Rac2, Rac2Authority.Primary.BuildId, $"LEVEL{level}"));
        _random = Rac2ParticleRandom.FromSeed(seed);
        _particles = new(_assets, _random);
        _trails = new(_assets, _random, _particles.ReserveIdentity);
        Textures = new[] { (92, 0), (92, 5), (92, 6), (97, 1), (97, 3), (97, 5), (53, 0), (90, 0), (90, 1), (23, 0) }
            .Select(frame => _assets.Definitions.Texture(frame.Item1, frame.Item2)).Distinct()
            .Select(texture => new RuntimeEffectTexture(TextureSource(texture), _assets.Textures[texture])).ToArray();
    }

    public void Emit(Vector3 origin, int instanceIndex, Vector3 cameraEye, Vector3 playerAnchor)
    {
        // Scale-one explosion: 3 -> 4 native units, life 30, no motion/gravity/spin.
        // The native integer opacity step adjusts the actual lifetime in Spawn85.
        var owner = _assets.Source with { AssetKind = "moby", OriginalId = $"505:{instanceIndex}" };
        Spawn(0, 0x388ef7, 100);
        Spawn(0, 0x0063f7, 100);
        for (int frame = 5; frame <= 6; frame++)
            for (int i = 0; i < 2; i++) Spawn(frame, 0x388ef7, 127);

        float cameraYaw = MathF.Atan2(cameraEye.Y - origin.Y, cameraEye.X - origin.X);
        // +20: ten paired fire/core layers; +24/+28: ten additional layers each.
        // The large paired layer is six times the first layer, not a soft flash.
        for (int group = 0; group < 3; group++)
            for (int i = 0; i < 10; i++)
            {
                int spin = (int)MathF.Round(_random.Symmetric(2, 5));
                int life = _random.Inclusive(30, 50);
                var position = M.Add(origin, new(_random.Symmetric(0, .1f), _random.Symmetric(0, .1f), _random.Symmetric(0, .1f)));
                float size = _random.Range(group == 1 ? .75f : .6f, group == 1 ? 1.5f : 1);
                float speed = _random.Range(9, 12);
                float yaw = cameraYaw + _random.Symmetric(0, 1.3962636f);
                var velocity = new Vector3(M.Multiply(M.Multiply(MathF.Cos(yaw), 1f / 60), speed),
                    M.Multiply(M.Multiply(MathF.Sin(yaw), 1f / 60), speed), M.Multiply(_random.Range(0, 12), 1f / 60));
                uint color = group switch { 0 => 0x407fff, 1 => 0x008fff, _ => 0x3064ff };
                int textureFrame = group switch { 0 => 1, 1 => 5, _ => 3 };
                Fire(position, velocity, life, spin, size, .01f, color, 127, textureFrame);
                if (group == 0)
                {
                    var outer = Fire(position, velocity, life, -spin * 2, size * 6, .02f, 0x0020ff, 48, 0);
                    if (outer is not null) SetTexture(outer, 53, 0);
                }
            }

        _trails.Emit(origin, cameraEye, playerAnchor, cameraYaw, Rac2ParticleSession.NativeCapacity - Count);

        void Spawn(int frame, uint color, byte opacity)
        {
            if (Count >= Rac2ParticleSession.NativeCapacity) return;
            var particle = _particles.Spawn85(owner, new(origin, Vector3.Zero, 30, color, 0, opacity, 0,
                3 * Rac2ParticleSession.EditorSizeUnits, 4 * Rac2ParticleSession.EditorSizeUnits, 0));
            if (particle is null) return;
            SetTexture(particle, 92, frame);
        }

        Rac2ParticleSession.Particle? Fire(Vector3 position, Vector3 velocity, int life, int spin,
            float size, float endSize, uint color, byte opacity, int frame)
        {
            if (Count >= Rac2ParticleSession.NativeCapacity) return null;
            return _particles.Spawn97(owner, new(position, velocity, life, color, spin, opacity, opacity,
                M.Multiply(size, Rac2ParticleSession.EditorSizeUnits), M.Multiply(endSize, Rac2ParticleSession.EditorSizeUnits), .004055556f), frame);
        }
    }

    private void SetTexture(Rac2ParticleSession.Particle particle, int definition, int frame)
    {
        particle.Texture = _assets.Definitions.Texture(definition, frame);
        particle.TextureSource = TextureSource(particle.Texture);
    }

    public void Step() { _particles.Step(); _trails.Step(); }
    public void Clear() { _particles.Clear(); _trails.Clear(); }

    /// <summary>Native depth/size projection through an explicit OBP camera calibration; GS rasterization is not emulated.</summary>
    public RuntimeEffectFrame Capture(RuntimeEffectView view)
    {
        var sprites = ImmutableArray.CreateBuilder<RuntimeEffectSprite>();
        foreach (var particle in _particles.Live)
            if (Project(particle.Identity, particle.Slot, particle.TextureSource, particle.Position, particle.NativeSize,
                particle.Color, particle.Angle, particle.DepthRange, view) is { } sprite) sprites.Add(sprite);
        _trails.Capture(view, sprites, out var meshes);
        return new(sprites.ToImmutable(), meshes, []);
    }

    internal static RuntimeEffectSprite? Project(long identity, int slot, ObpSourceRef texture, Vector3 position,
        float size, uint color, byte angle, byte depthRange, RuntimeEffectView view)
    {
        var runtimePosition = new Vector3(position.X, position.Z, position.Y);
        float depth = Vector3.Dot(runtimePosition - view.Eye, view.Forward);
        int z = (int)Math.Clamp((double)depth * 4096, int.MinValue, int.MaxValue);
        int near = (depthRange & 15) << 10, far = (depthRange & 0xf0) << 13;
        if (z <= near || z >= far || size <= 0) return null;
        int fade = Math.Min(4096, Math.Min(z - near, (far - z) >> 4));
        color = color & 0xffffff | (uint)(fade * (color >> 24) >> 12) << 24;
        float halfSize = size * view.ProjectionY / (2 * Rac2ParticleSession.EditorSizeUnits * (depth + .5f));
        float rotation = angle * MathF.Tau / 256;
        return new(identity, slot, texture, runtimePosition, size / Rac2ParticleSession.EditorSizeUnits,
            new((color & 255) / 128f, (color >> 8 & 255) / 128f, (color >> 16 & 255) / 128f, (color >> 24) / 128f),
            0, new(RuntimeBlendEquation.AdditiveSourceAlpha), RuntimeEffectSpriteProjection.ProjectedBillboard,
            new(MathF.Cos(rotation) * halfSize, MathF.Sin(rotation) * halfSize));
    }

    private ObpSourceRef TextureSource(int texture) => _assets.Source with
    { AssetKind = Rac2ParticleAssets.AssetKind, OriginalId = texture.ToString(CultureInfo.InvariantCulture) };
}
