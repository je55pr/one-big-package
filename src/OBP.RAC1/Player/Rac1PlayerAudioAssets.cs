using OBP.IO;
using OBP.RAC1.Animation;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;

namespace OBP.RAC1.Player;

public sealed record Rac1PlayerAudioAssets(
    IReadOnlyDictionary<int, Rac1AnimationSoundClip> Animations,
    IReadOnlyDictionary<int, Rac1CrateAudio.Sound> Footsteps)
{
    public static Rac1PlayerAudioAssets Decode(IRandomAccessReader disc,
        Rac1DiscIndex.NativeLevel level, Rac1LevelCore.Core core)
    {
        var ratchet = Rac1StaticClasses.ReadMobyTable(core).Single(row => row.OClass == 0);
        int joints = core.Assets[ratchet.AssetOffset + 8];
        var sequences = Rac1MobyAnimation.ReadRatchetSequences(core.Assets, core.Index,
            core.Header.RatchetSequencesOffset, joints);
        var sounds = sequences.Where(slot => slot.Value is { Frames.Count: > 0 })
            .ToDictionary(slot => slot.Index, slot => Rac1AnimationSoundClip.From(slot.Value!));
        var admitted = (from surface in Enumerable.Range(0, 4)
                        from foot in Enumerable.Range(0, 2)
                        from boots in Enumerable.Range(0, 2)
                        select Rac1PlayerAudioSession.FootstepIndex(level.LevelId, surface, foot, boots != 0)).ToHashSet();
        return new(sounds, Rac1CrateAudio.DecodeLevelSounds(disc, level, core, admitted));
    }
}
