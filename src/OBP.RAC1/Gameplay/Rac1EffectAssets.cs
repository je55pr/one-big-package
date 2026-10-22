using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>Distinct Part and Fx texture banks; indices are native level-local identities.</summary>
public sealed record Rac1EffectAssets(
    IReadOnlyDictionary<int, int[]> ParticleChoices,
    IReadOnlyDictionary<int, RuntimeTexture> PartTextures,
    IReadOnlyDictionary<int, RuntimeTexture> FxTextures)
{
    internal static Rac1EffectAssets Decode(Rac1LevelCore.Core core)
    {
        int[] types = [2, 4, 8, 11, 12, 15, 16, 24, 25, 27, 34];
        var choices = types.ToDictionary(type => type,
            type => Rac1ParticleAssets.TextureChoices(core, type, type == 12 ? 8 : type == 8 ? 10 : type == 16 ? 3 : 1));
        var parts = Rac1ParticleAssets.ReadTextures(core, choices.Values.SelectMany(ids => ids).ToHashSet())
            .ToDictionary(texture => texture.TextureId);
        return new(choices, parts, ReadFxTextures(core, new HashSet<int> { 14, 16 }));
    }

    public static IReadOnlyDictionary<int, RuntimeTexture> ReadFxTextures(Rac1LevelCore.Core core, IReadOnlySet<int> ids) =>
        Rac1FxTextures.Read(core, ids);
}
