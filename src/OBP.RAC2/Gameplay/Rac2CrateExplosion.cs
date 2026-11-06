using System.Globalization;
using System.Collections.Immutable;
using System.Numerics;
using OBP.Core;
using OBP.IO;
using OBP.PS2.Iso;
using OBP.RAC2.Level;
using OBP.Runtime.Effects;
using OBP.Runtime.Presentation;

namespace OBP.RAC2.Gameplay;

/// <summary>
/// The six type-85 flash sprites at Oozla 0x3F8F68..0x3F914C / Maktar 0x41F958.
/// Uses the active source's definition 92, frames 0/5/6 and the existing native
/// constructor/callback. Other explosion particle families remain unrecovered.
/// </summary>
public sealed class Rac2CrateExplosion
{
    private readonly Rac2ParticleAssets _assets;
    private readonly Rac2ParticleSession _particles;
    public IReadOnlyList<RuntimeEffectTexture> Textures { get; }
    public int Count => _particles.Count;

    public Rac2CrateExplosion(string path, int level, uint seed)
    {
        using var reader = new FileRandomAccessReader(path);
        var wad = Iso9660Filesystem.Open(reader).OpenFile($"/G/LEVEL{level}.WAD")
            ?? throw new InvalidDataException("Explosion source level is unavailable.");
        var header = Rac2LevelWad.ReadHeader(wad);
        _assets = Rac2ParticleAssets.Read(Rac2LevelCore.Open(Rac2LevelWad.RequireLump(wad, header, 0)),
            new(ObpSourceGame.Rac2, Rac2Authority.Primary.BuildId, $"LEVEL{level}"));
        _particles = new(_assets, Rac2ParticleRandom.FromSeed(seed));
        Textures = new[] { 0, 5, 6 }.Select(frame => _assets.Definitions.Texture(92, frame)).Distinct()
            .Select(texture => new RuntimeEffectTexture(TextureSource(texture), _assets.Textures[texture])).ToArray();
    }

    public void Emit(Vector3 origin, int instanceIndex)
    {
        // Scale-one explosion: 3 -> 4 native units, life 30, no motion/gravity/spin.
        // The native integer opacity step adjusts the actual lifetime in Spawn85.
        var owner = _assets.Source with { AssetKind = "moby", OriginalId = $"505:{instanceIndex}" };
        Spawn(0, 0x388ef7, 100);
        Spawn(0, 0x0063f7, 100);
        for (int frame = 5; frame <= 6; frame++)
            for (int i = 0; i < 2; i++) Spawn(frame, 0x388ef7, 127);

        void Spawn(int frame, uint color, byte opacity)
        {
            var particle = _particles.Spawn85(owner, new(origin, Vector3.Zero, 30, color, 0, opacity, 0,
                3 * Rac2ParticleSession.EditorSizeUnits, 4 * Rac2ParticleSession.EditorSizeUnits, 0));
            if (particle is null) return;
            particle.Texture = _assets.Definitions.Texture(92, frame);
            particle.TextureSource = TextureSource(particle.Texture);
        }
    }

    public void Step() => _particles.Step();
    public void Clear() => _particles.Clear();

    /// <summary>OBP world-size billboard projection; native camera/depth sorting and GS rasterization are not emulated.</summary>
    public RuntimeEffectFrame Capture() => new(_particles.Live.Select(particle =>
    {
        uint color = particle.Color;
        return new RuntimeEffectSprite(particle.Identity, particle.Slot, particle.TextureSource,
            new(particle.Position.X, particle.Position.Z, particle.Position.Y),
            particle.NativeSize / Rac2ParticleSession.EditorSizeUnits,
            new((color & 255) / 128f, (color >> 8 & 255) / 128f, (color >> 16 & 255) / 128f, (color >> 24) / 128f),
            particle.Angle * MathF.Tau / 256, new(RuntimeBlendEquation.AdditiveSourceAlpha));
    }).ToImmutableArray(), [], []);

    private ObpSourceRef TextureSource(int texture) => _assets.Source with
    { AssetKind = Rac2ParticleAssets.AssetKind, OriginalId = texture.ToString(CultureInfo.InvariantCulture) };
}
