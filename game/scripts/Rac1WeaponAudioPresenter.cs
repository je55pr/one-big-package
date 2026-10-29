using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Audio;

namespace OBP.Game;

internal readonly record struct Rac1WeaponAudioFacts(bool Admitted, Rac1WeaponId Weapon, int UseTicks,
    bool PyroEmitting, bool Vacuuming, bool Held, bool TeslaAmmo, ulong Tick, bool Trace);

/// <summary>Owns world-scoped R&C1 held-weapon loops and native Taunter timing.</summary>
internal sealed class Rac1WeaponAudioPresenter
{
    private readonly RuntimeAudioHost _rac1WeaponLoopAudio = new();
    private readonly RuntimeAudioHost _rac1TaunterAudio = new();
    private (int Class, int Sound)? _rac1WeaponLoop;
    private int _rac1TaunterSoundStep, _rac1TaunterSoundTicks;
    private readonly List<(ulong Tick, int Class, int Sound, bool Loop)> _rac1WeaponSoundTrace = [];

    internal void Reset(bool freeImmediately = false)
    {
        _rac1WeaponLoopAudio.StopAll(freeImmediately); _rac1WeaponLoop = null;
        _rac1TaunterAudio.StopAll(freeImmediately);
        _rac1TaunterSoundTicks = _rac1TaunterSoundStep = 0;
        _rac1WeaponSoundTrace.Clear();
    }

    internal (int Class, int Sound)? Loop => _rac1WeaponLoop;
    internal (ulong Tick, int Class, int Sound, bool Loop)[] Events => [.. _rac1WeaponSoundTrace];

    internal void Step(Rac1WeaponAudioFacts facts, PlayerHost? player, Rac1PickupRandom? random,
        IReadOnlyDictionary<(int Class, int LocalSound), Rac1CrateAudio.Sound>? sounds)
    {
        bool admitted = facts.Admitted && player is not null && random is not null;
        var weapon = facts.Weapon;
        (int Class, int Sound)? loop = admitted ? weapon switch
        {
            Rac1WeaponId.Blaster when facts.UseTicks > 0 => (168, 1),
            Rac1WeaponId.Pyrocitor when facts.PyroEmitting => (176, 1),
            Rac1WeaponId.SuckCannon when facts.Vacuuming => (849, 2),
            Rac1WeaponId.TeslaClaw when facts.Held && facts.TeslaAmmo => (177, 4),
            Rac1WeaponId.MorphORay when facts.Held => (185, 0),
            _ => null,
        } : null;
        if (loop != _rac1WeaponLoop)
        {
            _rac1WeaponLoopAudio.StopAll(); _rac1WeaponLoop = null;
            if (loop is { } cue && sounds?.TryGetValue(cue, out var sound) == true)
            {
                // Position is local to the player, so a sustained sound follows
                // its owner instead of remaining at the first shot's world point.
                if (_rac1WeaponLoopAudio.PlayEffect(player!, new(sound.SelectPitch(random!), RuntimeAudioCategory.SoundEffect,
                    loop: true, gain: sound.Gain, position: new(0, .7, 0))))
                {
                    _rac1WeaponLoop = cue;
                    Record(cue.Class, cue.Sound, facts.Tick, facts.Trace, true);
                }
            }
        }
        if (admitted && weapon == Rac1WeaponId.Taunter && facts.Held)
        {
            if (_rac1TaunterSoundTicks > 0) _rac1TaunterSoundTicks--;
            else if (sounds?.TryGetValue((175, _rac1TaunterSoundStep), out var sound) == true)
            {
                _rac1TaunterAudio.StopAll();
                if (_rac1TaunterAudio.PlayEffect(player!, new(sound.SelectPitch(random!), RuntimeAudioCategory.SoundEffect,
                    gain: sound.Gain, position: new(0, .7, 0)))) Record(175, _rac1TaunterSoundStep, facts.Tick, facts.Trace);
                _rac1TaunterSoundTicks = Math.Max(17, (int)Math.Ceiling(sound.Clip.DurationSeconds * 60));
                _rac1TaunterSoundStep = (_rac1TaunterSoundStep + 1) % 6;
            }
        }
        else { _rac1TaunterSoundTicks = 0; _rac1TaunterAudio.StopAll(); }
    }

    internal void Record(int cls, int sound, ulong tick, bool trace, bool loop = false)
    {
        if (!trace) return;
        if (_rac1WeaponSoundTrace.Count == 4096) _rac1WeaponSoundTrace.RemoveAt(0);
        _rac1WeaponSoundTrace.Add((tick, cls, sound, loop));
    }

    internal void SetPaused(bool paused)
    {
        _rac1WeaponLoopAudio.SetPaused(paused);
        _rac1TaunterAudio.SetPaused(paused);
    }
}
