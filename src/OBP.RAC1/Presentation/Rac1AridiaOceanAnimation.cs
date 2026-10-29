using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.RAC1.Presentation;

/// <summary>
/// Native class-854 Aridia ocean texture sequence and game-frame clock.
/// The overlay callback passes FX start 40, count 64 and 30 ticks per
/// frame to its VU1 setup. The source interpolates two neighbouring frames.
/// This supplies exact frame selection, not an invented GS pixel-blend mode.
/// </summary>
public static class Rac1AridiaOceanAnimation
{
    public const int TicksPerTextureFrame = 30;

    public readonly record struct FrameSample(int FirstTextureId, int NextTextureId, float NextWeight);

    public static FrameSample AtGameTick(long gameTick)
    {
        if (gameTick < 0)
            throw new ArgumentOutOfRangeException(nameof(gameTick));
        long frame = (gameTick / TicksPerTextureFrame) % Rac1AridiaOceanSource.AnimatedFxFrameCount;
        long next = (frame + 1) % Rac1AridiaOceanSource.AnimatedFxFrameCount;
        float blend = (gameTick % TicksPerTextureFrame) / (float)TicksPerTextureFrame;
        return new FrameSample(
            Rac1AridiaOceanSource.AnimatedFxStart + (int)frame,
            Rac1AridiaOceanSource.AnimatedFxStart + (int)next,
            blend);
    }

    /// <summary>
    /// Decode exactly the retail FX frames referenced by class 854.
    /// The host must not substitute tfrag textures or assume palette sharing.
    /// </summary>
    public static IReadOnlyList<RuntimeTexture> ReadSourceFrames(Rac1LevelCore.Core core)
    {
        ArgumentNullException.ThrowIfNull(core);
        var ids = Enumerable.Range(Rac1AridiaOceanSource.AnimatedFxStart,
            Rac1AridiaOceanSource.AnimatedFxFrameCount).ToHashSet();
        var source = Rac1FxTextures.Read(core, ids);
        var frames = new RuntimeTexture[Rac1AridiaOceanSource.AnimatedFxFrameCount];
        for (int i = 0; i < frames.Length; i++)
        {
            var texture = source[Rac1AridiaOceanSource.AnimatedFxStart + i];
            if (texture.Width != 64 || texture.Height != 64 ||
                texture.Rgba.Length != 64 * 64 * 4)
                throw new InvalidDataException($"Aridia ocean FX frame {texture.TextureId} has unexpected dimensions.");
            frames[i] = texture;
        }
        return frames;
    }
}