using OBP.Core;
using System.Buffers.Binary;
using System.Numerics;
using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Native planar steering and displacement producers: class749 0x2d54c8,
/// class1440 0x2e0e24. Coordinates are native Z-up; world contact is supplied by the host.
/// Reaction/contact resolution is not reimplemented as a second physics engine here.
/// </summary>
public sealed class Rac1EnemyMotion
{
    public const float Class749Scale = .75f;
    public const float Class749ContactRadius = .333f;
    private readonly Rac1MobySteering _yaw;
    private readonly Rac1MobySteering _travel = new();
    private readonly int _nativeClass;
    private float _vertical;
    public float Yaw => _yaw.Value;
    public float StepLength => Math.Max(0, _travel.Velocity);
    public void SetFacing(float yaw) => _yaw.SetValue(yaw);

    public Rac1EnemyMotion(int nativeClass, float yaw)
    {
        if (nativeClass is not (749 or 1440 or Rac1NovalisPeckbotSession.ClassId or Rac1NovalisBirdbotSession.ClassId)) throw new ArgumentOutOfRangeException(nameof(nativeClass));
        _nativeClass = nativeClass; _yaw = new(yaw);
    }

    public Vector3 Step(Vector3 position, Vector3 lookAt, Vector3? destination, bool blending,
        bool grounded, float yawOffset = 0, bool sweeping = false, float? distanceToPathEnd = null)
    {
        if (_nativeClass == 749 && blending && destination.HasValue) return Vector3.Zero;
        Vector3 direction = lookAt - position;
        if (_nativeClass == 749 && direction.Length() <= 2) yawOffset = 0;
        if (direction.X != 0 || direction.Y != 0)
        {
            float acceleration = (sweeping ? MathF.PI / 2 : MathF.PI * 4) / 3600;
            _yaw.Step(MathF.Atan2(direction.Y, direction.X) + yawOffset, acceleration, acceleration, MathF.PI * 4 / 60, angular: true);
        }
        if (destination is not { } target) return Vector3.Zero;
        Vector3 offset = target - position;
        float distance = distanceToPathEnd ?? offset.Length();
        _travel.SetValue(_nativeClass == 749 ? .5f : 0);
        _travel.Step(distance, _nativeClass == Rac1NovalisPeckbotSession.ClassId ? .3f / 60 : 8f / 3600,
            (_nativeClass == 749 ? 6f : 16f) / 3600,
            (_nativeClass == 749 ? 4f : _nativeClass == Rac1NovalisPeckbotSession.ClassId ? 2.7f : _nativeClass == Rac1NovalisBirdbotSession.ClassId ? 5f : 12f) / 60);
        if (_nativeClass is 1440 or Rac1NovalisPeckbotSession.ClassId or Rac1NovalisBirdbotSession.ClassId) return offset.LengthSquared() == 0 ? Vector3.Zero : Vector3.Normalize(offset) * StepLength;
        _vertical = grounded ? 0 : Math.Min(_vertical, 4f / 60);
        _vertical -= 10f / 3600;
        return new(MathF.Cos(Yaw) * StepLength, MathF.Sin(Yaw) * StepLength, _vertical);
    }

    /// <summary>0x257470 receives the authored reward copied from packed placement+0x10.</summary>
    public static int RewardCentre(RuntimeDynamicObject source)
    {
        if (source.SourceGame != ObpSourceGame.Rac1 || source.NativeClassId is not
            (749 or 1440 or Rac1SeekerSession.ClassId or Rac1AridiaEnemies.Sandshark or Rac1AridiaEnemies.Constructorbot or
                Rac1NovalisPeckbotSession.ClassId or Rac1NovalisBirdbotSession.ClassId))
            throw new ArgumentException("Source is outside the admitted enemy families.", nameof(source));
        var payloads = source.NativePayloads?.Where(payload => payload.Format == Rac1BoltCrate.InstancePayloadFormat).ToArray();
        if (payloads is not [{ Data.Length: 0x78 }]) throw new InvalidDataException("Enemy requires its exact authored placement record.");
        int value = BinaryPrimitives.ReadInt32LittleEndian(payloads[0].Data.AsSpan(0x10));
        _ = Rac1BoltCrate.RewardRange(value);
        return value;
    }
}
