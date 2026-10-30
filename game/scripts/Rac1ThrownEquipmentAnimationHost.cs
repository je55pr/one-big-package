using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;

namespace OBP.Game;

/// <summary>
/// World-lease animation clock shared by Bomb, Mine, Doom, Decoy and Drone throws.
/// Gameplay projectiles and persistent equipment inventory remain separate owners.
/// </summary>
internal sealed class Rac1ThrownEquipmentAnimationHost : IDisposable
{
    private long _generation;
    private bool _active;
    private bool _disposed;

    internal long Generation => _disposed ? 0 : _generation;
    internal bool Active => !_disposed && _active;

    internal void Begin()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _generation++;
        _active = true;
    }

    internal void Stop()
    {
        if (_disposed) return;
        _active = false;
    }

    internal void Reset()
    {
        if (_disposed) return;
        _generation = 0;
        _active = false;
    }

    internal Rac1LocomotionAnimationInput Select(Rac1LocomotionAnimationInput input,
        bool alive, bool wrenchActive, int? avatarSourceSequence, double traversalTime)
    {
        if (!_active || _disposed) return input;
        if (!alive || wrenchActive || input.Traversal is not null)
        {
            Stop();
            return input;
        }
        if (avatarSourceSequence == 44 && traversalTime >= Rac1BombProjectileSession.ThrowFinishTime)
        {
            Stop();
            return input with { GroundEntry = new(0, 0, _generation, BlendTicks: -1) };
        }
        return input with
        {
            GroundEntry = null,
            Traversal = new(0x23, 44, _generation,
                Rac1BombProjectileSession.ThrowEntryFrame, Rac1BombProjectileSession.ThrowBlendTicks)
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        Reset();
        _disposed = true;
    }
}
