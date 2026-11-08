using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>Distinct Part and Fx texture banks; indices are native level-local identities.</summary>
public sealed record Rac1EffectAssets(
    IReadOnlyDictionary<int, int[]> ParticleChoices,
    IReadOnlyDictionary<int, RuntimeTexture> PartTextures,
    IReadOnlyDictionary<int, RuntimeTexture> FxTextures)
{
    /// <summary>Loads first-frame Part bank entries selected by admitted class-27 PVars; uses the ordinary sprite renderer.</summary>
    public static Rac1EffectAssets DecodeEmitters(Rac1LevelCore.Core core, IEnumerable<Rac1ParticleEmitterParameters> parameters)
    {
        var choices = parameters.Select(p => { p.RequireSupported(); return (int)p.TextureDefinition; }).Distinct()
            .ToDictionary(type => type, type => Rac1ParticleAssets.TextureChoices(core, type, 1));
        var parts = Rac1ParticleAssets.ReadTextures(core, choices.Values.SelectMany(ids => ids).ToHashSet())
            .ToDictionary(texture => texture.TextureId);
        return new(choices, parts, new Dictionary<int, RuntimeTexture>());
    }

    internal static Rac1EffectAssets Decode(Rac1LevelCore.Core core)
    {
        int[] types = [2, 4, 8, 11, 12, 15, 16, 24, 25, 27, 34];
        var choices = types.ToDictionary(type => type,
            type => Rac1ParticleAssets.TextureChoices(core, type, type == 12 ? 8 : type == 8 ? 10 : type == 16 ? 3 : 1));
        var parts = Rac1ParticleAssets.ReadTextures(core, choices.Values.SelectMany(ids => ids).ToHashSet())
            .ToDictionary(texture => texture.TextureId);
        return new(choices, parts, ReadFxTextures(core, new HashSet<int> { 14, 16 }));
    }

    /// <summary>Part textures for the class-806 orb particles: table entries 62 (sparkle kinds 0 and 1) and 53 (glints).</summary>
    internal static Rac1EffectAssets DecodeNanotech(Rac1LevelCore.Core core)
    {
        var choices = new Dictionary<int, int[]>
        {
            [Rac1NanotechOrbAssets.ParticleTextureTable] = Rac1ParticleAssets.TextureChoices(core, Rac1NanotechOrbAssets.ParticleTextureTable, 2),
            [Rac1NanotechOrbAssets.GlintTextureTable] = Rac1ParticleAssets.TextureChoices(core, Rac1NanotechOrbAssets.GlintTextureTable, 1),
        };
        var parts = Rac1ParticleAssets.ReadTextures(core, choices.Values.SelectMany(ids => ids).ToHashSet())
            .ToDictionary(texture => texture.TextureId);
        return new(choices, parts, new Dictionary<int, RuntimeTexture>());
    }

    /// <summary>Part texture of the kind-0x45 spark (beam ends, robot lamp): lookup entry 69, one choice.</summary>
    public static Rac1EffectAssets DecodeSparks(Rac1LevelCore.Core core)
    {
        var choices = new Dictionary<int, int[]> { [Rac1ParticleSession.SparkTextureTable] = Rac1ParticleAssets.TextureChoices(core, Rac1ParticleSession.SparkTextureTable, 1) };
        var parts = Rac1ParticleAssets.ReadTextures(core, choices.Values.SelectMany(ids => ids).ToHashSet()).ToDictionary(texture => texture.TextureId);
        return new(choices, parts, new Dictionary<int, RuntimeTexture>());
    }

    /// <summary>Part textures of the Novalis pool bubble (type 0x38) and foam (type 0x39) particles: one choice each.</summary>
    public static Rac1EffectAssets DecodePoolBubbles(Rac1LevelCore.Core core)
    {
        var choices = new Dictionary<int, int[]>
        {
            [Rac1ParticleSession.PoolBubbleType] = Rac1ParticleAssets.TextureChoices(core, Rac1ParticleSession.PoolBubbleType, 1),
            [Rac1ParticleSession.PoolFoamType] = Rac1ParticleAssets.TextureChoices(core, Rac1ParticleSession.PoolFoamType, 1),
        };
        var parts = Rac1ParticleAssets.ReadTextures(core, choices.Values.SelectMany(ids => ids).ToHashSet()).ToDictionary(texture => texture.TextureId);
        return new(choices, parts, new Dictionary<int, RuntimeTexture>());
    }

    public static IReadOnlyDictionary<int, RuntimeTexture> ReadFxTextures(Rac1LevelCore.Core core, IReadOnlySet<int> ids) =>
        Rac1FxTextures.Read(core, ids);
}
