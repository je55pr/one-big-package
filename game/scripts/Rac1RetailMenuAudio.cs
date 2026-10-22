using Godot;
using OBP.Godot;
using OBP.Runtime.Audio;

namespace OneBigPackage;

/// <summary>Disc-derived startup-bank UI cue adapter. Cue pairing is provisional.</summary>
public sealed partial class Rac1RetailMenuAudio : Node
{
    private static Rac1RetailMenuAudio? _instance;
    private readonly RuntimeAudioHost _output = new();
    private readonly Dictionary<int, ulong> _lastSoundTime = [];

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _instance = this;
    }

    public static void Play(int sharedBankSlot)
    {
        if (_instance is not { } active || !IsInstanceValid(active)) return;
        if (Rac1RetailUiAssets.Sound(sharedBankSlot) is not { } clip) return;
        ulong now = Time.GetTicksMsec();
        if (active._lastSoundTime.TryGetValue(sharedBankSlot, out ulong last) && now - last < 85) return;
        active._lastSoundTime[sharedBankSlot] = now;
        active._output.PlayEffect(active,
            new RuntimeAudioPlaybackIntent(clip, RuntimeAudioCategory.SoundEffect, gain: .48));
    }

    public override void _ExitTree()
    {
        _output.StopAll();
        if (ReferenceEquals(_instance, this)) _instance = null;
    }
}
