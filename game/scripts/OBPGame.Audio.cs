using OBP.Runtime;
using OBP.Runtime.Audio;

namespace OneBigPackage;

public partial class OBPGame
{
    /// <summary>
    /// Exercise the neutral one-shot path with the source game's explicitly
    /// representative clip. This is an OBP integration cue, not a recovered
    /// native association between the clip and the triggering gameplay event.
    /// </summary>
    private void PlayRepresentativeAudioOneShot(RuntimeAudioPosition? position = null)
    {
        if (_world?.RepresentativeAudioOneShot is not { } clip)
        {
            return;
        }

        _worldHost.PlayEffect(new RuntimeAudioPlaybackIntent(
            clip,
            RuntimeAudioCategory.SoundEffect,
            position: position));
    }

    private static RuntimeAudioPosition? AudioPosition(RuntimeDynamicObject source)
    {
        double[] matrix = source.Transform.Matrix;
        return matrix.Length >= 16
            ? new RuntimeAudioPosition(matrix[12], matrix[13], matrix[14])
            : null;
    }
}
