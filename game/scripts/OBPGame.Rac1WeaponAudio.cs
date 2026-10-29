using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Audio;

namespace OBP.Game;

public partial class OBPGame
{
    private readonly RuntimeAudioHost _rac1WeaponLoopAudio = new();
    private readonly RuntimeAudioHost _rac1TaunterAudio = new();
    private (int Class, int Sound)? _rac1WeaponLoop;
    private int _rac1TaunterSoundStep, _rac1TaunterSoundTicks;
    private readonly List<(ulong Tick, int Class, int Sound, bool Loop)> _rac1WeaponSoundTrace = [];

    private void ResetRac1WeaponAudio(bool freeImmediately = false)
    {
        _rac1WeaponLoopAudio.StopAll(freeImmediately); _rac1WeaponLoop = null;
        _rac1TaunterAudio.StopAll(freeImmediately);
        _rac1TaunterSoundTicks = _rac1TaunterSoundStep = 0;
        _rac1WeaponSoundTrace.Clear();
    }

    private void StepRac1WeaponAudio()
    {
        bool admitted = _player is not null && _playerAudio?.Ready == true && PlayerLife.Alive && !MovementReproduction.IsLoading &&
            !_player.Rac1WrenchFrame.Active && !_rac1WrenchThrow.Active;
        var weapon = _rac1Weapons.Equipped;
        (int Class, int Sound)? loop = admitted ? weapon switch
        {
            Rac1WeaponId.Blaster when _rac1EquipmentUseTicks > 0 => (168, 1),
            Rac1WeaponId.Pyrocitor when _rac1PyroEmitting => (176, 1),
            Rac1WeaponId.SuckCannon when _rac1AdditionalWeapons?.Vacuuming == true => (849, 2),
            Rac1WeaponId.TeslaClaw when _rac1ArsenalHeld && _rac1Weapons.GetAmmo(19) > 0 => (177, 4),
            Rac1WeaponId.MorphORay when _rac1ArsenalHeld => (185, 0),
            _ => null,
        } : null;
        if (loop != _rac1WeaponLoop)
        {
            _rac1WeaponLoopAudio.StopAll(); _rac1WeaponLoop = null;
            if (loop is { } cue && _equipmentPresentation.Assets(OrdinaryPlayerSession, _rac1PickupAssets)?.Sounds?.TryGetValue(cue, out var sound) == true)
            {
                // Position is local to the player, so a sustained sound follows
                // its owner instead of remaining at the first shot's world point.
                if (_rac1WeaponLoopAudio.PlayEffect(_player!, new(sound.SelectPitch(_playerAudio!.Random), RuntimeAudioCategory.SoundEffect,
                    loop: true, gain: sound.Gain, position: new(0, .7, 0))))
                {
                    _rac1WeaponLoop = cue;
                    RecordRac1WeaponSound(cue.Class, cue.Sound, true);
                }
            }
        }
        if (admitted && weapon == Rac1WeaponId.Taunter && _rac1ArsenalHeld)
        {
            if (_rac1TaunterSoundTicks > 0) _rac1TaunterSoundTicks--;
            else if (_equipmentPresentation.Assets(OrdinaryPlayerSession, _rac1PickupAssets)?.Sounds?.TryGetValue((175, _rac1TaunterSoundStep), out var sound) == true)
            {
                _rac1TaunterAudio.StopAll();
                if (_rac1TaunterAudio.PlayEffect(_player!, new(sound.SelectPitch(_playerAudio!.Random), RuntimeAudioCategory.SoundEffect,
                    gain: sound.Gain, position: new(0, .7, 0)))) RecordRac1WeaponSound(175, _rac1TaunterSoundStep);
                _rac1TaunterSoundTicks = Math.Max(17, (int)Math.Ceiling(sound.Clip.DurationSeconds * 60));
                _rac1TaunterSoundStep = (_rac1TaunterSoundStep + 1) % 6;
            }
        }
        else { _rac1TaunterSoundTicks = 0; _rac1TaunterAudio.StopAll(); }
    }

    private void RecordRac1WeaponSound(int cls, int sound, bool loop = false)
    {
        if (!_args.Rac1WeaponsSmoke) return;
        _rac1WeaponSoundTrace.Add((_rac1WeaponWorldTick, cls, sound, loop));
    }

    private void SetPlayerAudioPaused(bool paused)
    {
        // Suspend playback without rewinding native cue cursors, reselecting
        // probability programs or allocating duplicate looping voices.
        _playerAudio?.SetPaused(paused);
        _rac1WeaponLoopAudio.SetPaused(paused);
        _rac1TaunterAudio.SetPaused(paused);
        FusionWeapons.SetPaused(paused);
    }
}
