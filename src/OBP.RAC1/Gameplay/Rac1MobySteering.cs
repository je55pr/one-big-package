namespace OBP.RAC1.Gameplay;

/// <summary>Scalar acceleration/braking recurrence shared by 0x25b970 and angular 0x25be00.</summary>
public sealed class Rac1MobySteering
{
    public float Value { get; private set; }
    public float Velocity { get; private set; }
    public Rac1MobySteering(float value = 0) => SetValue(value);
    public void SetValue(float value) => Value = float.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(nameof(value));

    public float Step(float target, float acceleration, float deceleration, float maximumVelocity, bool angular = false)
    {
        if (!float.IsFinite(target) || !float.IsFinite(acceleration) || !float.IsFinite(deceleration) || !float.IsFinite(maximumVelocity) ||
            !(acceleration > 0) || !(deceleration > 0) || maximumVelocity < 0)
            throw new ArgumentOutOfRangeException(nameof(acceleration));
        float error = angular ? Wrap(target - Value) : target - Value;
        if (Velocity * error >= 0 && error != 0)
        {
            float stopping = .5f * Velocity * Velocity / deceleration;
            if (Math.Abs(error) < stopping)
            {
                float remaining = Math.Abs(error) + Math.Abs(Velocity);
                Velocity = Approach(Velocity, 0, stopping < remaining ? deceleration : deceleration * 1.1f);
            }
            else Velocity = Approach(Velocity, MathF.CopySign(Math.Min(MathF.Sqrt(2 * deceleration * Math.Abs(error)), maximumVelocity), error), acceleration);
            if (Math.Abs(Velocity) > Math.Abs(error)) Value = target;
            else Value += Velocity;
        }
        else { Velocity = Approach(Velocity, 0, deceleration); Value += Velocity; }
        if (angular) Value = Wrap(Value);
        return Value;
    }

    private static float Approach(float value, float target, float step) => value < target ? Math.Min(value + step, target) : Math.Max(value - step, target);
    public static float Wrap(float radians) => MathF.IEEERemainder(radians, 2 * MathF.PI);
}
